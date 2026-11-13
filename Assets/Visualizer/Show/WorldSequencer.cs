using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Visualizer
{
    /// <summary>
    /// Plays several worlds (scenes) over one song and crossfades between them at the cue points you list.
    /// Each world keeps running on the same song clock, so a world you return to has carried on moving.
    /// Which world is showing, and how far through a fade we are, depends only on the song time.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    [AddComponentMenu("Visualizer/World Sequencer")]
    public class WorldSequencer : MonoBehaviour
    {
        public enum TimeUnit { Seconds, Bars }

        public enum FadeTiming
        {
            [Tooltip("The fade begins at the cue.")] StartsAtCue,
            [Tooltip("The fade is half over at the cue.")] CentredOnCue,
            [Tooltip("The new world is fully in at the cue.")] EndsAtCue
        }

        [Serializable]
        public class Cue
        {
            [Tooltip("Scene name, exactly as in File > Build Profiles > Scene List (e.g. CubeMonolith).")]
            public string scene;
            [Tooltip("When to switch to this world (bar number or seconds, see Time Unit). Bars start at 1; 17.5 = halfway through bar 17.")]
            public float at = 1f;
            [Tooltip("How long the crossfade lasts, in the same unit. 0 = hard cut.")]
            [Min(0)] public float fade = 4f;
        }

        [Tooltip("Leave empty to use the SongPlayer in this scene.")]
        public SongPlayer song;

        [Header("Cues")]
        [Tooltip("Whether 'At' and 'Fade' below are in bars of the song or in seconds.")]
        public TimeUnit timeUnit = TimeUnit.Bars;
        public FadeTiming fadeTiming = FadeTiming.CentredOnCue;
        public List<Cue> cues = new List<Cue>();

        [Header("Crossfade look")]
        [Tooltip("0 = plain even fade. 1 = the new world seeps in through drifting cloud shapes.")]
        [Range(0, 1)] public float cloudiness = 0.7f;
        [Tooltip("Number of cloud shapes across the picture: low = big soft blobs, high = fine smoke.")]
        [Min(0.1f)] public float cloudSize = 2f;
        [Tooltip("How fast the cloud shapes drift during a fade.")]
        public float cloudDrift = 0.05f;
        [Tooltip("How much the new world's lights arrive before its dark areas.")]
        [Range(0, 1)] public float lightBleed = 0.25f;
        [Tooltip("Low = crisp edges between the two worlds, high = soft and smoky.")]
        [Range(0.02f, 1)] public float softness = 0.7f;
        [Tooltip("How much the picture ripples mid-fade.")]
        [Range(0, 0.1f)] public float warp = 0.015f;
        [Tooltip("Extra brightness where the two worlds meet.")]
        [Range(0, 2)] public float edgeGlow = 0.5f;

        [Header("Setup")]
        public Shader blendShader;
        [Tooltip("Seconds a world is switched on before its fade begins, so its fog and lighting have settled.")]
        [Min(0)] public float preRoll = 0.75f;
        [Tooltip("Start the song as soon as every world has loaded.")]
        public bool playWhenLoaded = true;

        class World
        {
            public string name;
            public GameObject[] roots;
            public Camera camera;
            public Light[] lights;
            public bool[] lightWasOn;
            public RenderTexture target;
            public bool active = true;
        }

        struct Segment
        {
            public World world;
            public double start, end;
        }

        readonly Dictionary<string, World> _worlds = new Dictionary<string, World>();
        readonly List<Segment> _timeline = new List<Segment>();
        readonly HashSet<World> _wanted = new HashSet<World>();
        SongPlayer _song;
        Material _material;
        RawImage _image;
        bool _ready;

        /// <summary>Name of the world currently being faded to or shown.</summary>
        public string CurrentWorld { get; private set; }
        /// <summary>0..1 progress of the current crossfade (1 = finished).</summary>
        public float Blend { get; private set; }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endContextRendering += OnEndContextRendering;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
            RestoreLights();
        }

        // Two worlds are drawn at once during a fade and they sit in the same space, so while one world's
        // camera is drawing, every other world's lights are switched off. Otherwise they would light each other.
        void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            World owner = null;
            foreach (var world in _worlds.Values)
                if (world != null && world.camera == cam) { owner = world; break; }
            if (owner == null) return;
            foreach (var world in _worlds.Values)
            {
                if (world == null || world.lights == null) continue;
                for (int i = 0; i < world.lights.Length; i++)
                    if (world.lights[i] != null) world.lights[i].enabled = world == owner && world.lightWasOn[i];
            }
        }

        void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras) => RestoreLights();

        void RestoreLights()
        {
            foreach (var world in _worlds.Values)
            {
                if (world == null || world.lights == null) continue;
                for (int i = 0; i < world.lights.Length; i++)
                    if (world.lights[i] != null) world.lights[i].enabled = world.lightWasOn[i];
            }
        }

        IEnumerator Start()
        {
            _song = song != null ? song : GetComponentInParent<SongPlayer>();
            if (_song == null)
                foreach (var root in gameObject.scene.GetRootGameObjects())
                    if ((_song = root.GetComponentInChildren<SongPlayer>(true)) != null) break;
            if (_song == null) { Debug.LogError("[WorldSequencer] No SongPlayer in this scene.", this); yield break; }
            if (blendShader == null) blendShader = Shader.Find("Visualizer/WorldBlend");
            if (blendShader == null) { Debug.LogError("[WorldSequencer] Blend shader is missing.", this); yield break; }

            BuildOutput();

            foreach (var cue in cues)
            {
                if (string.IsNullOrWhiteSpace(cue.scene) || _worlds.ContainsKey(cue.scene)) continue;
                _worlds[cue.scene] = null; // reserved; filled in by OnSceneLoaded
                AsyncOperation load = null;
                try { load = SceneManager.LoadSceneAsync(cue.scene, LoadSceneMode.Additive); }
                catch (Exception e) { Debug.LogError($"[WorldSequencer] Could not load scene '{cue.scene}': {e.Message}", this); }
                if (load == null)
                {
                    Debug.LogError($"[WorldSequencer] Scene '{cue.scene}' was not found. Check the spelling and that it is in the Scene List (File > Build Profiles).", this);
                    continue;
                }
                yield return load;
            }

            _song.MakeCurrent();
            BuildTimeline();
            _ready = true;
            if (playWhenLoaded) _song.Play();
        }

        // Runs as soon as a world has loaded, before anything in it starts playing.
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Additive || !_worlds.ContainsKey(scene.name) || _worlds[scene.name] != null) return;

            int index = 0;
            foreach (var w in _worlds.Values) if (w != null) index++;
            int layer = LayerMask.NameToLayer("World " + (index + 1));
            if (layer < 0) layer = 24 + index;

            var world = new World { name = scene.name, roots = scene.GetRootGameObjects() };
            foreach (var root in world.roots)
            {
                // Each world gets its own layer, so its lights, fog and sky only show up in its own camera.
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                // One song clock and one pair of ears for the whole show.
                foreach (var player in root.GetComponentsInChildren<SongPlayer>(true)) player.enabled = false;
                foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                {
                    if (world.camera == null || cam.CompareTag("MainCamera")) world.camera = cam;
                }
            }

            if (world.camera == null)
                Debug.LogError($"[WorldSequencer] World '{scene.name}' has no camera.", this);
            else
            {
                world.camera.cullingMask = 1 << layer;
                if (world.camera.TryGetComponent(out HDAdditionalCameraData hd)) hd.volumeLayerMask = 1 << layer;
            }

            var lights = new List<Light>();
            foreach (var root in world.roots) lights.AddRange(root.GetComponentsInChildren<Light>(true));
            world.lights = lights.ToArray();
            world.lightWasOn = new bool[world.lights.Length];
            for (int i = 0; i < world.lights.Length; i++) world.lightWasOn[i] = world.lights[i].enabled;

            _worlds[scene.name] = world;
            SetActive(world, false);
        }

        void BuildOutput()
        {
            var go = new GameObject("World Blend Output", typeof(Canvas), typeof(RawImage));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -100; // anything else on screen (lyrics later) draws on top
            _material = new Material(blendShader) { hideFlags = HideFlags.DontSave };
            _image = go.GetComponent<RawImage>();
            _image.raycastTarget = false;
            _image.material = _material;
            _image.texture = Texture2D.blackTexture;
            var rect = _image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        void BuildTimeline()
        {
            _timeline.Clear();
            foreach (var cue in cues)
            {
                if (string.IsNullOrWhiteSpace(cue.scene) || !_worlds.TryGetValue(cue.scene, out var world) || world == null) continue;
                float from = cue.at;
                if (fadeTiming == FadeTiming.CentredOnCue) from -= cue.fade * 0.5f;
                else if (fadeTiming == FadeTiming.EndsAtCue) from -= cue.fade;
                double start = ToSeconds(from), end = ToSeconds(from + cue.fade);
                if (start < 0) { end -= start; start = 0; } // the opening fade can't begin before the song does
                _timeline.Add(new Segment { world = world, start = start, end = end });
            }
            _timeline.Sort((a, b) => a.start.CompareTo(b.start));

            var report = new System.Text.StringBuilder("[WorldSequencer] Cue times:");
            foreach (var s in _timeline)
                report.Append($"\n  {s.world.name}: fades in {s.start:0.00}s - {s.end:0.00}s");
            Debug.Log(report.ToString(), this);
        }

        double ToSeconds(float value)
        {
            if (timeUnit == TimeUnit.Seconds) return value;
            var map = _song.TempoMap;
            if (map == null) return value;
            float bar = Mathf.Max(0f, value - 1f); // bar 1 is the start of the song
            int whole = Mathf.FloorToInt(bar);
            double a = map.BarStartSeconds(whole), b = map.BarStartSeconds(whole + 1);
            return a + (b - a) * (bar - whole) + _song.midiOffset;
        }

        void Update()
        {
            if (!_ready) return;
            double t = _song.SongTime;

            int i = -1;
            for (int k = 0; k < _timeline.Count; k++)
                if (_timeline[k].start <= t) i = k;

            _wanted.Clear();
            World from = null, to = null;
            float blend = 1f;
            if (i >= 0)
            {
                var segment = _timeline[i];
                to = segment.world;
                from = i > 0 ? _timeline[i - 1].world : null;
                if (segment.end > segment.start) blend = Mathf.Clamp01((float)((t - segment.start) / (segment.end - segment.start)));
                if (from == to) blend = 1f;
                _wanted.Add(to);
                if (blend < 1f && from != null) _wanted.Add(from);
            }
            // Warm up whichever world comes next.
            if (i + 1 < _timeline.Count && _timeline[i + 1].start - t <= preRoll) _wanted.Add(_timeline[i + 1].world);

            foreach (var world in _worlds.Values)
                if (world != null) SetActive(world, _wanted.Contains(world));

            EnsureTargets();

            Blend = blend;
            CurrentWorld = to != null ? to.name : null;
            Texture toTexture = to != null && to.target != null ? to.target : Texture2D.blackTexture;
            Texture fromTexture = blend >= 1f ? toTexture : (from != null && from.target != null ? from.target : Texture2D.blackTexture);
            _image.texture = fromTexture;
            _material.SetTexture("_ToTex", toTexture);
            _material.SetFloat("_Blend", Mathf.SmoothStep(0f, 1f, blend));
            _material.SetFloat("_Cloudiness", cloudiness);
            _material.SetFloat("_CloudSize", cloudSize);
            _material.SetFloat("_LightBleed", lightBleed);
            _material.SetFloat("_Softness", softness);
            _material.SetFloat("_Warp", warp);
            _material.SetFloat("_EdgeGlow", edgeGlow);
            // Different cloud shapes for every fade, drifting with the song clock.
            _material.SetVector("_NoiseOffset", new Vector4(i * 13.7f + (float)t * cloudDrift, i * 5.3f - (float)t * cloudDrift * 0.6f, 0f, 0f));
        }

        void SetActive(World world, bool active)
        {
            if (world.active == active) return;
            world.active = active;
            foreach (var root in world.roots)
                if (root != null) root.SetActive(active);
        }

        void EnsureTargets()
        {
            int w = Mathf.Max(16, Screen.width), h = Mathf.Max(16, Screen.height);
            foreach (var world in _worlds.Values)
            {
                if (world == null || world.camera == null || !world.active) continue;
                if (world.target != null && world.target.width == w && world.target.height == h) continue;
                if (world.target != null) { world.camera.targetTexture = null; world.target.Release(); Destroy(world.target); }
                world.target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = world.name + " picture" };
                world.camera.targetTexture = world.target;
            }
        }

        void OnDestroy()
        {
            foreach (var world in _worlds.Values)
                if (world != null && world.target != null) { world.target.Release(); Destroy(world.target); }
            if (_material != null) Destroy(_material);
        }
    }
}
