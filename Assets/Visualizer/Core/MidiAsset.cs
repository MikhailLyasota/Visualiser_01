using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Visualizer
{
    /// <summary>
    /// A .mid file imported into Unity. Created automatically when you drop a .mid/.midi file into the project.
    /// </summary>
    public class MidiAsset : ScriptableObject
    {
        [SerializeField, HideInInspector] byte[] data;

        [Tooltip("What the importer found in the file: tracks, notes used, tempo and time-signature changes.")]
        [SerializeField, TextArea(6, 40)] string summary;

        public string Summary => summary;

        public MidiSong Parse() => MidiSong.Parse(data);

        public void SetData(byte[] bytes)
        {
            data = bytes;
            try { summary = BuildSummary(MidiSong.Parse(bytes)); }
            catch (System.Exception e) { summary = "Could not read this MIDI file: " + e.Message; }
        }

        static string BuildSummary(MidiSong song)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Format {song.format}, {song.ticksPerQuarter} ticks per quarter, length {song.LengthSeconds:0.00} s");
            sb.AppendLine($"Tempo changes: {song.tempoMap.TempoChangeCount}   Time signatures: " +
                          string.Join(", ", song.tempoMap.TimeSignatures.Select(s => $"{s.numerator}/{s.denominator} @ bar {s.bar + 1}")));
            foreach (var track in song.tracks)
            {
                sb.AppendLine();
                sb.AppendLine($"Track {track.index}: \"{track.name}\" - {track.notes.Count} notes");
                if (track.notes.Count == 0) continue;
                var counts = new SortedDictionary<int, int>();
                var channels = new SortedSet<int>();
                foreach (var n in track.notes)
                {
                    counts.TryGetValue(n.note, out int c);
                    counts[n.note] = c + 1;
                    channels.Add(n.channel + 1);
                }
                sb.AppendLine("  Channels: " + string.Join(", ", channels));
                sb.AppendLine("  Notes used (note x count): " + string.Join(", ", counts.Select(kv => $"{kv.Key} x{kv.Value}")));
            }
            return sb.ToString();
        }
    }
}
