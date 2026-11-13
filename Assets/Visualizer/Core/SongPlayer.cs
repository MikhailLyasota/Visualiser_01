using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Visualizer
{
    /// <summary>One occurrence of a named event (a drum hit, a note).</summary>
    public struct Hit
    {
        /// <summary>Seconds from the start of the MIDI file.</summary>
        public double time;
        /// <summary>How long the note was held, in seconds.</summary>
        public double duration;
        /// <summary>0..1.</summary>
        public float velocity;
        public long tick;
        public int note;
    }

    /// <summary>
    /// The single song clock. Loads the MIDI, turns notes into named events through the mappings, plays the
    /// audio, and tells reactors what time it is. Everything visual is computed from <see cref="SongTime"/>.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class SongPlayer : MonoBehaviour
    {
        /// <summary>The active song in the scene. Reactors use this when no player is assigned to them.</summary>
        public static SongPlayer Current { get; private set; }

        [Header("Song")]
        [Tooltip("A .mid file dropped into the project.")]
        public MidiAsset midi;
        [Tooltip("Note-number -> event-name tables. Each one applies to the tracks/channel it is set up for.")]
        public List<MidiMapping> mappings = new List<MidiMapping>();
        [Tooltip("The rendered mix (optional). The clock follows it during preview.")]
        public AudioClip audioClip;
        [Tooltip("Seconds added to every MIDI event to line it up with the audio. Positive = visuals react later.")]
        public double midiOffset = 0;

        [Header("Playback")]
        public bool playOnStart = true;
        [Tooltip("Where in the song playback starts, in seconds.")]
        public double startTime = 0;

        [Header("Scrubbing")]
        [Tooltip("Freeze playback and show the song at exactly 'Manual Song Time'.")]
        public bool manualTime = false;
        public double manualSongTime = 0;

        /// <summary>Seconds since the start of the song (audio timeline).</summary>
        public double SongTime { get; private set; }
        /// <summary>The same moment on the MIDI file's timeline (song time minus the offset).</summary>
        public double MidiTime => SongTime - midiOffset;
        public MidiSong Song { get; private set; }
        public TempoMap TempoMap => Song?.tempoMap;
        public bool IsPlaying { get; private set; }

        static readonly List<Hit> NoHits = new List<Hit>();
        readonly Dictionary<string, List<Hit>> _events = new Dictionary<string, List<Hit>>();
        AudioSource _audio;
        double _anchor;      // game time at which song time would be zero
        int _startFrame;

        public IEnumerable<string> EventNames => _events.Keys;

        void OnEnable()
        {
            Current = this;
            Load();
        }

        void OnDisable()
        {
            if (Current == this) Current = null;
        }

        /// <summary>Makes this the song that reactors follow when several SongPlayers are loaded.</summary>
        public void MakeCurrent() => Current = this;

        void Start()
        {
            if (playOnStart) Play();
        }

        /// <summary>(Re)reads the MIDI asset and rebuilds the named-event lists.</summary>
        public void Load()
        {
            _events.Clear();
            Song = null;
            if (midi == null) return;

            try { Song = midi.Parse(); }
            catch (Exception e)
            {
                Debug.LogError($"[SongPlayer] Could not read MIDI '{midi.name}': {e.Message}", this);
                return;
            }

            var map = Song.tempoMap;
            var unmapped = new SortedDictionary<string, SortedSet<int>>();
            foreach (var track in Song.tracks)
            {
                var active = mappings.Where(m => m != null && m.MatchesTrack(track)).ToList();
                if (active.Count == 0) continue;

                foreach (var n in track.notes)
                {
                    bool found = false;
                    foreach (var mapping in active)
                    {
                        if (!mapping.MatchesChannel(n.channel) || !mapping.TryGetEvent(n.note, out string eventName)) continue;
                        if (!_events.TryGetValue(eventName, out var list)) _events[eventName] = list = new List<Hit>();
                        double start = map.TicksToSeconds(n.tick);
                        list.Add(new Hit
                        {
                            time = start,
                            duration = map.TicksToSeconds(n.tick + n.durationTicks) - start,
                            velocity = n.velocity / 127f,
                            tick = n.tick,
                            note = n.note
                        });
                        found = true;
                        break;
                    }
                    if (!found)
                    {
                        string key = $"track {track.index} \"{track.name}\"";
                        if (!unmapped.TryGetValue(key, out var set)) unmapped[key] = set = new SortedSet<int>();
                        set.Add(n.note);
                    }
                }
            }

            foreach (var list in _events.Values) list.Sort((a, b) => a.time.CompareTo(b.time));

            foreach (var kv in unmapped)
                Debug.LogWarning($"[SongPlayer] Notes with no mapping in {kv.Key}: {string.Join(", ", kv.Value)}. " +
                                 "Add them to a MIDI Mapping asset if they should trigger visuals.", this);
        }

        public void Play()
        {
            SongTime = startTime;
            _anchor = Time.timeAsDouble - startTime;
            _startFrame = Time.frameCount;
            IsPlaying = true;

            if (audioClip != null)
            {
                if (_audio == null && !TryGetComponent(out _audio)) _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.clip = audioClip;
                if (startTime >= 0 && startTime < audioClip.length)
                {
                    _audio.time = (float)startTime;
                    _audio.Play();
                }
            }
        }

        public void Stop()
        {
            IsPlaying = false;
            if (_audio != null) _audio.Stop();
        }

        void Update()
        {
            if (manualTime)
            {
                SongTime = manualSongTime;
                return;
            }
            if (!IsPlaying) return;

            if (Time.captureFramerate > 0)
            {
                // Offline render: frame N is exactly N / fps. No drift, fully repeatable.
                SongTime = startTime + (double)(Time.frameCount - _startFrame) / Time.captureFramerate;
            }
            else if (_audio != null && _audio.isPlaying)
            {
                // Live preview: follow the audio. Its position only updates in small steps, so glide between
                // them and snap if we ever get noticeably out of sync (e.g. after an editor hitch).
                double audioTime = (double)_audio.timeSamples / audioClip.frequency;
                double predicted = SongTime + Time.unscaledDeltaTime;
                SongTime = Math.Abs(predicted - audioTime) > 0.05 ? audioTime : predicted + (audioTime - predicted) * 0.1;
                _anchor = Time.timeAsDouble - SongTime;
            }
            else
            {
                SongTime = Time.timeAsDouble - _anchor;
            }
        }

        /// <summary>All hits of an event, sorted by time. Empty if the event never occurs.</summary>
        public IReadOnlyList<Hit> GetHits(string eventName)
        {
            return eventName != null && _events.TryGetValue(eventName, out var list) ? list : NoHits;
        }

        /// <summary>Index of the most recent hit at or before <paramref name="midiTime"/>, or -1.</summary>
        public int IndexOfLastHit(string eventName, double midiTime)
        {
            if (eventName == null || !_events.TryGetValue(eventName, out var list)) return -1;
            int lo = 0, hi = list.Count - 1, result = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (list[mid].time <= midiTime) { result = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return result;
        }

        public bool TryGetLastHit(string eventName, double midiTime, out Hit hit)
        {
            int i = IndexOfLastHit(eventName, midiTime);
            hit = i >= 0 ? _events[eventName][i] : default;
            return i >= 0;
        }
    }
}
