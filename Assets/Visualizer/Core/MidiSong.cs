using System;
using System.Collections.Generic;
using System.Text;

namespace Visualizer
{
    public struct MidiNote
    {
        public long tick;
        public long durationTicks;
        /// <summary>0-15.</summary>
        public byte channel;
        public byte note;
        /// <summary>1-127.</summary>
        public byte velocity;
    }

    public struct MidiControlChange
    {
        public long tick;
        public byte channel;
        public byte controller;
        public byte value;
    }

    public sealed class MidiTrack
    {
        public int index;
        public string name = "";
        public readonly List<MidiNote> notes = new List<MidiNote>();
        public readonly List<MidiControlChange> controlChanges = new List<MidiControlChange>();
    }

    /// <summary>A parsed Standard MIDI File: tracks of notes stored by absolute tick, plus the tempo map.</summary>
    public sealed class MidiSong
    {
        public int format;
        public int ticksPerQuarter;
        public long lengthTicks;
        public readonly List<MidiTrack> tracks = new List<MidiTrack>();
        public TempoMap tempoMap;

        public double LengthSeconds => tempoMap.TicksToSeconds(lengthTicks);

        /// <summary>Parses a Standard MIDI File (format 0 or 1).</summary>
        public static MidiSong Parse(byte[] data)
        {
            if (data == null || data.Length < 14) throw new FormatException("Not a MIDI file (too short).");
            int pos = 0;
            if (ReadId(data, ref pos) != "MThd") throw new FormatException("Not a MIDI file (missing MThd header).");
            int headerLength = ReadInt32(data, ref pos);
            int headerEnd = pos + headerLength;
            var song = new MidiSong { format = ReadInt16(data, ref pos) };
            ReadInt16(data, ref pos); // declared track count; we trust the chunks instead
            int division = ReadInt16(data, ref pos);
            if ((division & 0x8000) != 0)
                throw new NotSupportedException("SMPTE-timed MIDI files are not supported. Export with ticks-per-quarter (PPQ) timing.");
            song.ticksPerQuarter = division;
            pos = headerEnd;

            var tempos = new List<(long, int)>();
            var signatures = new List<(long, int, int)>();

            while (pos + 8 <= data.Length)
            {
                string id = ReadId(data, ref pos);
                int length = ReadInt32(data, ref pos);
                int end = Math.Min(data.Length, pos + length);
                if (id == "MTrk")
                {
                    var track = new MidiTrack { index = song.tracks.Count };
                    long trackEnd = ParseTrack(data, pos, end, track, tempos, signatures);
                    song.lengthTicks = Math.Max(song.lengthTicks, trackEnd);
                    song.tracks.Add(track);
                }
                pos = end;
            }

            song.tempoMap = new TempoMap(song.ticksPerQuarter, tempos, signatures);
            return song;
        }

        static long ParseTrack(byte[] d, int pos, int end, MidiTrack track,
            List<(long, int)> tempos, List<(long, int, int)> signatures)
        {
            long tick = 0;
            int runningStatus = 0;
            // Notes currently held, keyed by channel*128+note -> indices into track.notes (oldest first).
            var open = new Dictionary<int, Queue<int>>();

            while (pos < end)
            {
                tick += ReadVariableLength(d, ref pos, end);
                if (pos >= end) break;

                int status = d[pos];
                if (status < 0x80)
                {
                    if (runningStatus == 0) throw new FormatException("Corrupt MIDI track (data byte without status).");
                    status = runningStatus;
                }
                else pos++;

                if (status == 0xFF) // meta event
                {
                    int type = d[pos++];
                    int length = (int)ReadVariableLength(d, ref pos, end);
                    if (type == 0x51 && length >= 3)
                        tempos.Add((tick, (d[pos] << 16) | (d[pos + 1] << 8) | d[pos + 2]));
                    else if (type == 0x58 && length >= 2)
                        signatures.Add((tick, d[pos], 1 << d[pos + 1]));
                    else if (type == 0x03 && string.IsNullOrEmpty(track.name))
                        track.name = Encoding.UTF8.GetString(d, pos, Math.Min(length, end - pos)).Trim('\0', ' ');
                    pos += length;
                    if (type == 0x2F) break; // end of track
                    continue;
                }
                if (status == 0xF0 || status == 0xF7) // sysex
                {
                    int length = (int)ReadVariableLength(d, ref pos, end);
                    pos += length;
                    continue;
                }

                runningStatus = status;
                int kind = status & 0xF0;
                byte channel = (byte)(status & 0x0F);
                byte data1 = d[pos++];
                byte data2 = (kind == 0xC0 || kind == 0xD0) ? (byte)0 : d[pos++];

                if (kind == 0x90 && data2 > 0)
                {
                    int key = channel * 128 + data1;
                    if (!open.TryGetValue(key, out var queue)) open[key] = queue = new Queue<int>();
                    queue.Enqueue(track.notes.Count);
                    track.notes.Add(new MidiNote { tick = tick, channel = channel, note = data1, velocity = data2 });
                }
                else if (kind == 0x80 || kind == 0x90)
                {
                    int key = channel * 128 + data1;
                    if (open.TryGetValue(key, out var queue) && queue.Count > 0)
                    {
                        int i = queue.Dequeue();
                        var n = track.notes[i];
                        n.durationTicks = tick - n.tick;
                        track.notes[i] = n;
                    }
                }
                else if (kind == 0xB0)
                {
                    track.controlChanges.Add(new MidiControlChange { tick = tick, channel = channel, controller = data1, value = data2 });
                }
            }

            // Notes never released: hold them to the end of the track.
            foreach (var queue in open.Values)
                foreach (int i in queue)
                {
                    var n = track.notes[i];
                    n.durationTicks = tick - n.tick;
                    track.notes[i] = n;
                }
            return tick;
        }

        static string ReadId(byte[] d, ref int pos)
        {
            string s = Encoding.ASCII.GetString(d, pos, 4);
            pos += 4;
            return s;
        }

        static int ReadInt32(byte[] d, ref int pos)
        {
            int v = (d[pos] << 24) | (d[pos + 1] << 16) | (d[pos + 2] << 8) | d[pos + 3];
            pos += 4;
            return v;
        }

        static int ReadInt16(byte[] d, ref int pos)
        {
            int v = (d[pos] << 8) | d[pos + 1];
            pos += 2;
            return v;
        }

        static long ReadVariableLength(byte[] d, ref int pos, int end)
        {
            long v = 0;
            while (pos < end)
            {
                byte b = d[pos++];
                v = (v << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0) break;
            }
            return v;
        }
    }
}
