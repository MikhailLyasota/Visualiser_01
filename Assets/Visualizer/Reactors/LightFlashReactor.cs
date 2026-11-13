using UnityEngine;

namespace Visualizer
{
    /// <summary>
    /// Flashes a Light on a MIDI event. Set the Light up the way it should look at the peak of a full-strength
    /// hit; this component dims it between hits.
    /// </summary>
    [RequireComponent(typeof(Light))]
    [AddComponentMenu("Visualizer/Light Flash Reactor")]
    public class LightFlashReactor : MidiReactor
    {
        [Header("Light")]
        [Tooltip("Brightness between hits, as a fraction of the Light's own intensity. 0 = dark.")]
        [Min(0)] public float restBrightness = 0f;
        [Tooltip("Brightness at the peak of a hit, as a fraction of the Light's own intensity.")]
        [Min(0)] public float flashBrightness = 1f;

        [Tooltip("Also shift the colour on each hit.")]
        public bool changeColor = false;
        public Color restColor = Color.white;
        public Color flashColor = Color.white;

        Light _light;
        float _authoredIntensity;
        Color _authoredColor;

        void OnEnable()
        {
            _light = GetComponent<Light>();
            _authoredIntensity = _light.intensity;
            _authoredColor = _light.color;
        }

        void OnDisable()
        {
            if (_light == null) return;
            _light.intensity = _authoredIntensity;
            _light.color = _authoredColor;
        }

        protected override void Apply(float level)
        {
            _light.intensity = _authoredIntensity * Mathf.LerpUnclamped(restBrightness, flashBrightness, level);
            if (changeColor) _light.color = Color.LerpUnclamped(restColor, flashColor, level);
        }
    }
}
