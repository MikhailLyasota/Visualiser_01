using System;
using System.Collections.Generic;
using UnityEngine;

namespace Visualizer
{
    /// <summary>
    /// Translates raw MIDI note numbers into named events ("Kick", "Snare"...). Visuals only ever see the names,
    /// so swapping drum libraries means swapping this asset, not touching the scene.
    /// </summary>
    [CreateAssetMenu(fileName = "MidiMapping", menuName = "Visualizer/MIDI Mapping")]
    public class MidiMapping : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string eventName;
            [Tooltip("Every MIDI note number that should trigger this event (e.g. all kick articulations).")]
            public int[] notes;
        }

        [Header("Which tracks this mapping applies to")]
        [Tooltip("Only tracks whose name contains this text (case-insensitive). Leave empty for any track.")]
        public string trackNameContains = "";
        [Tooltip("Only this track number (as listed in the MIDI asset's summary). -1 = any track.")]
        public int trackIndex = -1;
        [Tooltip("Only this MIDI channel, 1-16. 0 = any channel.")]
        [Range(0, 16)] public int channel = 0;

        [Header("Note -> event")]
        public List<Entry> entries = new List<Entry>();

        Dictionary<int, string> _lookup;

        void OnEnable() => _lookup = null;
        void OnValidate() => _lookup = null;

        public bool MatchesTrack(MidiTrack track)
        {
            if (trackIndex >= 0 && track.index != trackIndex) return false;
            if (!string.IsNullOrEmpty(trackNameContains) &&
                (track.name ?? "").IndexOf(trackNameContains, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        /// <param name="midiChannel">0-15, as stored in the file.</param>
        public bool MatchesChannel(int midiChannel) => channel == 0 || channel == midiChannel + 1;

        public bool TryGetEvent(int note, out string eventName)
        {
            if (_lookup == null)
            {
                _lookup = new Dictionary<int, string>();
                foreach (var e in entries)
                {
                    if (e == null || string.IsNullOrWhiteSpace(e.eventName) || e.notes == null) continue;
                    foreach (int n in e.notes)
                        if (!_lookup.ContainsKey(n)) _lookup[n] = e.eventName.Trim();
                }
            }
            return _lookup.TryGetValue(note, out eventName);
        }

        [ContextMenu("Reset to General MIDI Drums")]
        public void LoadGeneralMidiDrums()
        {
            entries = new List<Entry>
            {
                E("Kick", 35, 36),
                E("Snare", 38, 40),
                E("SideStick", 37),
                E("Clap", 39),
                E("HiHatClosed", 42),
                E("HiHatPedal", 44),
                E("HiHatOpen", 46),
                E("Tom1", 50),
                E("Tom2", 48),
                E("Tom3", 47),
                E("Tom4", 45),
                E("FloorTom1", 43),
                E("FloorTom2", 41),
                E("Crash", 49, 57),
                E("Ride", 51, 59),
                E("RideBell", 53),
                E("China", 52),
                E("Splash", 55),
                E("Tambourine", 54),
                E("Cowbell", 56),
            };
            _lookup = null;
        }

        static Entry E(string name, params int[] notes) => new Entry { eventName = name, notes = notes };
    }
}
