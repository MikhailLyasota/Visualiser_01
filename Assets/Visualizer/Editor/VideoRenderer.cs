using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Visualizer.Editor
{
    /// <summary>
    /// Renders the open scene to a video file: enters Play mode, waits for the song to be ready, records every
    /// frame at a fixed frame rate (however long each frame takes to draw), then stops at the end of the song.
    /// </summary>
    [InitializeOnLoad]
    public static class VideoRenderer
    {
        const string ActiveKey = "Visualizer.VideoRenderer.Active";
        const string SettingsPath = "Assets/Visualizer/Rendering/VideoRenderSettings.asset";

        enum Phase { Idle, WaitingForSong, WarmingUp, Recording }

        static Phase _phase;
        static RecorderController _controller;
        static VideoRenderSettings _settings;
        static SongPlayer _song;
        static double _start, _end;
        static int _warmUpUntilFrame;
        static string _outputFile;
        static int _progressId = -1;
        static double _startedAt;

        static VideoRenderer()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Visualizer/Render Current Scene to Video", priority = 0)]
        static void Render()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[VideoRenderer] Leave Play mode first, then start the render.");
                return;
            }
            LoadOrCreateSettings();
            SessionState.SetBool(ActiveKey, true);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("Visualizer/Stop Render", priority = 1)]
        static void StopRender()
        {
            if (_phase == Phase.Idle && !SessionState.GetBool(ActiveKey, false)) return;
            Finish("stopped by you");
        }

        [MenuItem("Visualizer/Video Render Settings", priority = 2)]
        static void SelectSettings()
        {
            Selection.activeObject = LoadOrCreateSettings();
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        static VideoRenderSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<VideoRenderSettings>(SettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<VideoRenderSettings>();
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(ActiveKey, false))
            {
                _phase = Phase.WaitingForSong;
                _settings = LoadOrCreateSettings();
            }
            else if (change == PlayModeStateChange.ExitingPlayMode && _phase != Phase.Idle)
            {
                Finish("Play mode was stopped", exitPlayMode: false);
            }
        }

        static void Tick()
        {
            if (_phase == Phase.Idle || !EditorApplication.isPlaying) return;

            // Unity stops drawing new frames when it is not the app in front. Asking for the next frame here
            // keeps the render going while you work in another window.
            EditorApplication.QueuePlayerLoopUpdate();

            if (_phase == Phase.WaitingForSong)
            {
                // The song starts once every world has loaded.
                var song = SongPlayer.Current;
                if (song == null || !song.IsPlaying) return;
                _song = song;
                _start = _settings.startSeconds;
                _end = _settings.endSeconds > 0 ? _settings.endSeconds : song.LengthSeconds + _settings.tailSeconds;
                if (_end <= _start) { Finish($"the end ({_end:0.0}s) is not after the start ({_start:0.0}s)"); return; }

                // Hold the picture still at the start time while fog and lighting settle.
                song.Stop();
                song.startTime = _start;
                song.manualSongTime = _start;
                song.manualTime = true;
                _warmUpUntilFrame = Time.frameCount + _settings.warmUpFrames;
                _phase = Phase.WarmingUp;
                return;
            }

            if (_phase == Phase.WarmingUp)
            {
                if (Time.frameCount < _warmUpUntilFrame) return;
                if (!StartRecorder()) { Finish("the recorder could not start (see the Console)"); return; }
                _song.manualTime = false;
                _song.PlayFromNextFrame();
                _startedAt = EditorApplication.timeSinceStartup;
                _progressId = Progress.Start("Rendering video", Path.GetFileName(_outputFile));
                _phase = Phase.Recording;
                return;
            }

            if (_phase == Phase.Recording)
            {
                double t = _song != null ? _song.SongTime : _end;
                float done = Mathf.Clamp01((float)((t - _start) / (_end - _start)));
                if (_progressId >= 0)
                {
                    double elapsed = EditorApplication.timeSinceStartup - _startedAt;
                    string left = done > 0.01f ? $", about {FormatTime(elapsed / done - elapsed)} left" : "";
                    Progress.Report(_progressId, done, $"{t - _start:0.0}s of {_end - _start:0.0}s{left}");
                }
                if (t >= _end || _controller == null || !_controller.IsRecording()) Finish(null);
            }
        }

        static bool StartRecorder()
        {
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                string.IsNullOrWhiteSpace(_settings.outputFolder) ? "Renders" : _settings.outputFolder);
            Directory.CreateDirectory(folder);
            string name = $"{SceneManager.GetActiveScene().name}_{DateTime.Now:yyyyMMdd_HHmmss}";

            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "Visualizer Movie";
            movie.Enabled = true;
            if (_settings.format == VideoRenderSettings.Format.MP4)
            {
                var encoder = new CoreEncoderSettings { Codec = CoreEncoderSettings.OutputCodec.MP4 };
                if (_settings.bitrateMbps > 0)
                {
                    encoder.EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Custom;
                    encoder.TargetBitRate = _settings.bitrateMbps;
                    encoder.EncodingProfile = CoreEncoderSettings.H264EncodingProfile.High;
                }
                else
                {
                    encoder.EncodingQuality = (CoreEncoderSettings.VideoEncodingQuality)(int)_settings.quality;
                }
                movie.EncoderSettings = encoder;
            }
            else
            {
                movie.EncoderSettings = new ProResEncoderSettings
                {
                    Format = _settings.format == VideoRenderSettings.Format.ProRes4444
                        ? ProResEncoderSettings.OutputFormat.ProRes4444
                        : ProResEncoderSettings.OutputFormat.ProRes422HQ
                };
            }
            // The crossfade is drawn as a screen overlay, so record the finished Game view rather than one camera.
            movie.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = _settings.width - _settings.width % 2,
                OutputHeight = _settings.height - _settings.height % 2
            };
            movie.CaptureAudio = _settings.includeAudio;
            movie.OutputFile = Path.Combine(folder, name);
            _outputFile = movie.OutputFile + "." + movie.EncoderSettings.Extension;

            var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            controllerSettings.AddRecorderSettings(movie);
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
            controllerSettings.FrameRate = _settings.frameRate;
            controllerSettings.CapFrameRate = false; // draw as fast (or as slowly) as the machine can
            controllerSettings.ExitPlayMode = false;

            _controller = new RecorderController(controllerSettings);
            _controller.PrepareRecording();
            return _controller.StartRecording();
        }

        static void Finish(string problem, bool exitPlayMode = true)
        {
            bool wasRecording = _phase == Phase.Recording;
            _phase = Phase.Idle;
            SessionState.SetBool(ActiveKey, false);

            if (_controller != null)
            {
                if (_controller.IsRecording()) _controller.StopRecording();
                _controller = null;
            }
            if (_progressId >= 0)
            {
                Progress.Remove(_progressId);
                _progressId = -1;
            }

            if (problem != null && !wasRecording)
                Debug.LogError($"[VideoRenderer] Nothing was rendered: {problem}.");
            else if (wasRecording)
            {
                string took = FormatTime(EditorApplication.timeSinceStartup - _startedAt);
                string note = problem != null ? $" (ended early: {problem})" : "";
                Debug.Log($"[VideoRenderer] Video saved{note}: {_outputFile}\nRender took {took}.");
                if (problem == null) EditorUtility.RevealInFinder(_outputFile);
            }

            if (exitPlayMode && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }

        static string FormatTime(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{span.Minutes}m {span.Seconds}s";
        }
    }
}
