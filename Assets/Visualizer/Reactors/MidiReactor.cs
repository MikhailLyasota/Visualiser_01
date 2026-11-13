using UnityEngine;

namespace Visualizer
{
    /// <summary>
    /// Base for anything that reacts to a named MIDI event. Each frame it works out a 0..1 "level" purely from
    /// how long ago the event last happened, so any frame looks the same no matter how you got there
    /// (playing, scrubbing or offline rendering).
    /// </summary>
    public abstract class MidiReactor : MonoBehaviour
    {
        [Header("Trigger")]
        [Tooltip("Leave empty to use the SongPlayer in the scene.")]
        public SongPlayer song;
        [Tooltip("Event name from the MIDI Mapping, e.g. Kick, Snare, HiHatClosed, Crash.")]
        public string eventName = "Kick";

        [Header("Envelope")]
        [Tooltip("Seconds to rise to full after the hit. 0 = instant.")]
        [Min(0)] public float attack = 0f;
        [Tooltip("Seconds to fade back to rest after the peak.")]
        [Min(0.001f)] public float decay = 0.25f;
        [Tooltip("0 = every hit is full strength. 1 = soft hits (low MIDI velocity) flash proportionally weaker.")]
        [Range(0, 1)] public float velocitySensitivity = 1f;

        /// <summary>Current 0..1 level of the reaction.</summary>
        public float Level { get; private set; }

        protected virtual void Update()
        {
            Level = Evaluate();
            Apply(Level);
        }

        float Evaluate()
        {
            var player = song != null ? song : SongPlayer.Current;
            if (player == null) return 0f;

            double now = player.MidiTime;
            int i = player.IndexOfLastHit(eventName, now);
            if (i < 0) return 0f;

            var hits = player.GetHits(eventName);
            float level = HitLevel(hits[i], now);
            // The previous hit may still be ringing while this one rises.
            if (i > 0) level = Mathf.Max(level, HitLevel(hits[i - 1], now));
            return level;
        }

        float HitLevel(in Hit hit, double now)
        {
            float t = (float)(now - hit.time);
            float shape;
            if (t < attack) shape = t / attack;
            else
            {
                float x = Mathf.Clamp01((t - attack) / decay);
                shape = (1f - x) * (1f - x);
            }
            return shape * Mathf.Lerp(1f, hit.velocity, velocitySensitivity);
        }

        /// <param name="level">0 = at rest, 1 = full hit.</param>
        protected abstract void Apply(float level);
    }
}
