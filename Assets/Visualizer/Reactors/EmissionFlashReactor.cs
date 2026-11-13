using UnityEngine;

namespace Visualizer
{
    /// <summary>
    /// Makes an object's own glow (material emission) flash on a MIDI event. Works on any mesh whose material
    /// is HDRP Lit; it does not change the shared material, only this object.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    [AddComponentMenu("Visualizer/Emission Flash Reactor")]
    public class EmissionFlashReactor : MidiReactor
    {
        [Header("Glow")]
        [ColorUsage(false)] public Color color = Color.white;
        [Tooltip("Glow strength between hits (0 = off).")]
        [Min(0)] public float restBrightness = 20f;
        [Tooltip("Glow strength at the peak of a hit.")]
        [Min(0)] public float flashBrightness = 3000f;
        [Tooltip("Shader property to drive. HDRP Lit uses _EmissiveColor.")]
        public string property = "_EmissiveColor";

        Renderer _renderer;
        MaterialPropertyBlock _block;

        void OnEnable()
        {
            _renderer = GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        void OnDisable()
        {
            if (_renderer != null) _renderer.SetPropertyBlock(null);
        }

        protected override void Apply(float level)
        {
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(property, color.linear * Mathf.LerpUnclamped(restBrightness, flashBrightness, level));
            _renderer.SetPropertyBlock(_block);
        }
    }
}
