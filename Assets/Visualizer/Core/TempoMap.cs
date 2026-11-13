using System;
using System.Collections.Generic;

namespace Visualizer
{
    /// <summary>Where a moment sits on the real bar/beat grid (from the file's time-signature events).</summary>
    public struct BarPosition
    {
        /// <summary>0-based bar index.</summary>
        public int bar;
        /// <summary>0-based beat inside the bar, in units of the time signature's denominator. Fractional.</summary>
        public double beat;
        /// <summary>0..1 progress through the bar. Multiply by N for a custom subdivision (e.g. 7 pulses per bar).</summary>
        public double barPhase;
        public int numerator;
        public int denominator;
    }

    /// <summary>
    /// Converts between MIDI ticks, seconds and bars/beats, honoring every tempo and time-signature change.
    /// </summary>
    public sealed class TempoMap
    {
        struct TempoSegment
        {
            public long tick;
            public int microsecondsPerQuarter;
            public double seconds;
        }

        public struct TimeSignatureSegment
        {
            public long tick;
            public int numerator;
            public int denominator;
            /// <summary>0-based index of the bar that starts at <see cref="tick"/>.</summary>
            public int bar;
        }

        public readonly int ticksPerQuarter;
        readonly List<TempoSegment> _tempos = new List<TempoSegment>();
        readonly List<TimeSignatureSegment> _signatures = new List<TimeSignatureSegment>();

        public int TempoChangeCount => _tempos.Count;
        public IReadOnlyList<TimeSignatureSegment> TimeSignatures => _signatures;

        public TempoMap(int ticksPerQuarter,
            List<(long tick, int microsecondsPerQuarter)> tempoEvents,
            List<(long tick, int numerator, int denominator)> timeSignatureEvents)
        {
            if (ticksPerQuarter <= 0) throw new ArgumentException("ticksPerQuarter must be positive");
            this.ticksPerQuarter = ticksPerQuarter;

            // --- tempo ---
            var tempos = new List<(long tick, int us)>(tempoEvents);
            tempos.Sort((a, b) => a.tick.CompareTo(b.tick));
            if (tempos.Count == 0 || tempos[0].tick > 0) tempos.Insert(0, (0, 500000)); // MIDI default: 120 BPM

            for (int i = 0; i < tempos.Count; i++)
            {
                var seg = new TempoSegment { tick = tempos[i].tick, microsecondsPerQuarter = tempos[i].us };
                if (_tempos.Count > 0)
                {
                    var prev = _tempos[_tempos.Count - 1];
                    if (prev.tick == seg.tick)
                    {
                        seg.seconds = prev.seconds;
                        _tempos[_tempos.Count - 1] = seg;
                        continue;
                    }
                    seg.seconds = prev.seconds + (seg.tick - prev.tick) * (prev.microsecondsPerQuarter / 1e6) / ticksPerQuarter;
                }
                _tempos.Add(seg);
            }

            // --- time signatures ---
            var sigs = new List<(long tick, int num, int den)>(timeSignatureEvents);
            sigs.Sort((a, b) => a.tick.CompareTo(b.tick));
            if (sigs.Count == 0 || sigs[0].tick > 0) sigs.Insert(0, (0, 4, 4));

            for (int i = 0; i < sigs.Count; i++)
            {
                var seg = new TimeSignatureSegment
                {
                    tick = sigs[i].tick,
                    numerator = Math.Max(1, sigs[i].num),
                    denominator = Math.Max(1, sigs[i].den)
                };
                if (_signatures.Count > 0)
                {
                    var prev = _signatures[_signatures.Count - 1];
                    if (prev.tick == seg.tick)
                    {
                        seg.bar = prev.bar;
                        _signatures[_signatures.Count - 1] = seg;
                        continue;
                    }
                    // A signature change always starts a new bar, even if the previous bar was cut short.
                    double bars = (seg.tick - prev.tick) / TicksPerBar(prev);
                    seg.bar = prev.bar + (int)Math.Ceiling(bars - 1e-9);
                }
                _signatures.Add(seg);
            }
        }

        double TicksPerBar(in TimeSignatureSegment s) => ticksPerQuarter * 4.0 * s.numerator / s.denominator;

        public double TicksToSeconds(double tick)
        {
            var seg = _tempos[FindTempoByTick(tick)];
            return seg.seconds + (tick - seg.tick) * (seg.microsecondsPerQuarter / 1e6) / ticksPerQuarter;
        }

        public double SecondsToTicks(double seconds)
        {
            var seg = _tempos[FindTempoBySeconds(seconds)];
            return seg.tick + (seconds - seg.seconds) * ticksPerQuarter / (seg.microsecondsPerQuarter / 1e6);
        }

        /// <summary>Tempo in quarter-note BPM at the given time.</summary>
        public double BpmAtSeconds(double seconds) => 60e6 / _tempos[FindTempoBySeconds(seconds)].microsecondsPerQuarter;

        public BarPosition GetBarPosition(double tick)
        {
            var seg = _signatures[FindSignatureByTick(tick)];
            double ticksPerBar = TicksPerBar(seg);
            double barsIn = Math.Max(0, tick - seg.tick) / ticksPerBar;
            int wholeBars = (int)Math.Floor(barsIn + 1e-9);
            double phase = Math.Max(0, barsIn - wholeBars);
            return new BarPosition
            {
                bar = seg.bar + wholeBars,
                barPhase = phase,
                beat = phase * seg.numerator,
                numerator = seg.numerator,
                denominator = seg.denominator
            };
        }

        public BarPosition GetBarPositionAtSeconds(double seconds) => GetBarPosition(SecondsToTicks(seconds));

        /// <summary>Tick at which the given 0-based bar starts.</summary>
        public double BarStartTick(int bar)
        {
            int i = _signatures.Count - 1;
            while (i > 0 && _signatures[i].bar > bar) i--;
            var seg = _signatures[i];
            return seg.tick + (bar - seg.bar) * TicksPerBar(seg);
        }

        public double BarStartSeconds(int bar) => TicksToSeconds(BarStartTick(bar));

        int FindTempoByTick(double tick)
        {
            int lo = 0, hi = _tempos.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_tempos[mid].tick <= tick) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        int FindTempoBySeconds(double seconds)
        {
            int lo = 0, hi = _tempos.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_tempos[mid].seconds <= seconds) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        int FindSignatureByTick(double tick)
        {
            int lo = 0, hi = _signatures.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_signatures[mid].tick <= tick) lo = mid; else hi = mid - 1;
            }
            return lo;
        }
    }
}
