using UnityEngine;

namespace Visualizer
{
    /// <summary>
    /// How the show is rendered to a video file. Used by the menu Visualizer > Render Current Scene to Video.
    /// </summary>
    [CreateAssetMenu(fileName = "VideoRenderSettings", menuName = "Visualizer/Video Render Settings")]
    public class VideoRenderSettings : ScriptableObject
    {
        public enum Format
        {
            [Tooltip("Small file, plays everywhere. Good for uploads and previews.")] MP4,
            [Tooltip("Very large file, near-lossless. Good if you will edit or grade the video afterwards.")] ProRes422HQ,
            [Tooltip("Largest file, highest quality.")] ProRes4444
        }

        public enum Quality { Low, Medium, High }

        [Header("Picture")]
        [Min(16)] public int width = 1920;
        [Min(16)] public int height = 1080;
        [Tooltip("Frames per second of the finished video.")]
        [Min(1)] public float frameRate = 60f;

        [Header("File")]
        public Format format = Format.MP4;
        [Tooltip("MP4 only.")]
        public Quality quality = Quality.High;
        [Tooltip("MP4 only. Megabits per second; 0 = let Quality decide. Dark, foggy scenes band less at 30-50.")]
        [Min(0)] public float bitrateMbps = 40f;
        public bool includeAudio = true;
        [Tooltip("Folder for finished videos, inside the project folder (not inside Assets).")]
        public string outputFolder = "Renders";

        [Header("Range")]
        [Tooltip("Where in the song the video starts, in seconds.")]
        [Min(0)] public float startSeconds = 0f;
        [Tooltip("Where the video ends, in seconds. 0 = the end of the song.")]
        [Min(0)] public float endSeconds = 0f;
        [Tooltip("Extra seconds recorded after the end of the song (only when End Seconds is 0).")]
        [Min(0)] public float tailSeconds = 2f;

        [Header("Advanced")]
        [Tooltip("Frames drawn at the start time before recording begins, so fog and lighting have settled.")]
        [Range(0, 120)] public int warmUpFrames = 30;
    }
}
