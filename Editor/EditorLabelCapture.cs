/*
 * ====================================================================================
 * EditorLabelCapture.cs
 * Author: Nathanael Hondi
 * Copyright: © 2026 NPIRE EXE. Licensed under the MIT License.
 * Description: Records part of an editor window (the Inspector by default, or any open
 *              window such as a custom tool) while GameplayRecorder records, so the real
 *              editor controls can be shown as a label on the gameplay video. Each
 *              segment gets <name>_label.mp4 beside it, and, when ffmpeg is installed,
 *              <name>_labelled.mp4 with the label burned into a corner. Pixels come from
 *              the window's own panel, so other windows covering it don't matter.
 * ====================================================================================
 */
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Media;
using UnityEditorInternal;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NPIREEXE.Capture.Editor
{
    [InitializeOnLoad]
    public static class EditorLabelCapture
    {
        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

        /// <summary>Label frames per second. Editor windows repaint far slower than gameplay.</summary>
        public const int FrameRate = 30;

        private const string Pref = "NPIREEXE.Capture.Label.";

        /// <summary>Record the label alongside gameplay recordings.</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(Pref + "Enabled", false);
            set => EditorPrefs.SetBool(Pref + "Enabled", value);
        }

        /// <summary>Title of the editor window to record, as shown on its tab.</summary>
        public static string WindowTitle
        {
            get => EditorPrefs.GetString(Pref + "Window", "Inspector");
            set => EditorPrefs.SetString(Pref + "Window", value);
        }

        /// <summary>Area to record, in points from the top-left of the window's panel (tab bar included). Zero size = whole panel.</summary>
        public static Rect Area
        {
            get => new Rect(EditorPrefs.GetFloat(Pref + "X", 0f), EditorPrefs.GetFloat(Pref + "Y", 0f),
                            EditorPrefs.GetFloat(Pref + "W", 0f), EditorPrefs.GetFloat(Pref + "H", 0f));
            set
            {
                EditorPrefs.SetFloat(Pref + "X", value.x);
                EditorPrefs.SetFloat(Pref + "Y", value.y);
                EditorPrefs.SetFloat(Pref + "W", value.width);
                EditorPrefs.SetFloat(Pref + "H", value.height);
            }
        }

        public static Corner LabelCorner
        {
            get => (Corner)EditorPrefs.GetInt(Pref + "Corner", (int)Corner.BottomRight);
            set => EditorPrefs.SetInt(Pref + "Corner", (int)value);
        }

        /// <summary>Label size on the video: 1 = the window's size in points at 1080, scaled with the output.</summary>
        public static float LabelScale
        {
            get => EditorPrefs.GetFloat(Pref + "Scale", 1.5f);
            set => EditorPrefs.SetFloat(Pref + "Scale", Mathf.Clamp(value, 0.25f, 4f));
        }

        /// <summary>After each segment, burn the label into a copy of the gameplay video with ffmpeg.</summary>
        public static bool BurnIn
        {
            get => EditorPrefs.GetBool(Pref + "BurnIn", true);
            set => EditorPrefs.SetBool(Pref + "BurnIn", value);
        }

        /// <summary>ffmpeg executable. Empty = look on PATH.</summary>
        public static string FfmpegPath
        {
            get => EditorPrefs.GetString(Pref + "Ffmpeg", "");
            set => EditorPrefs.SetString(Pref + "Ffmpeg", value);
        }

        public static bool IsCapturing => _encoder != null;

        private static MediaEncoder _encoder;
        private static Texture2D _frame;
        private static int _startFrame;
        private static long _written;
        private static EditorWindow _window;
        private static Rect _areaPoints;
        private static Vector2Int _size;
        private static Vector2 _sizePoints;
        private static string _labelPath, _gameplayPath;

        static EditorLabelCapture()
        {
            GameplayRecorder.RecordingStateChanged += OnRecordingStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += () => Finish(compose: false);
        }

        // ------------------------------------------------------------------ windows and pixels

        public static EditorWindow FindWindow(string title) =>
            Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w != null && w.titleContent.text == title);

        /// <summary>Titles of every open editor window, for the picker.</summary>
        public static string[] OpenWindowTitles() =>
            Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Where(w => w != null && !string.IsNullOrEmpty(w.titleContent.text))
                .Select(w => w.titleContent.text).Distinct().OrderBy(t => t).ToArray();

        private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        private static readonly FieldInfo ParentField = typeof(EditorWindow).GetField("m_Parent", Any);
        private static readonly MethodInfo GrabPixelsMethod = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView")?.GetMethod("GrabPixels", Any);
        private static Color32[] _raw, _cropped;

        /// <summary>The panel (docked area or floating window) that draws <paramref name="window"/>, in screen points.</summary>
        public static Rect PanelRect(EditorWindow window)
        {
            object parent = ParentField?.GetValue(window);
            PropertyInfo screen = parent?.GetType().GetProperty("screenPosition", Any);
            return screen != null ? (Rect)screen.GetValue(parent) : window.position;
        }

        /// <summary>Size in pixels (even numbers) of the current area of <paramref name="window"/>.</summary>
        public static Vector2Int AreaPixels(EditorWindow window, out Rect areaPoints)
        {
            Rect panel = PanelRect(window);
            areaPoints = Area;
            if (areaPoints.width < 8f || areaPoints.height < 8f) areaPoints = new Rect(0f, 0f, panel.width, panel.height);
            float ppp = EditorGUIUtility.pixelsPerPoint;
            return new Vector2Int(Mathf.Max(2, Mathf.RoundToInt(areaPoints.width * ppp) / 2 * 2),
                                  Mathf.Max(2, Mathf.RoundToInt(areaPoints.height * ppp) / 2 * 2));
        }

        /// <summary>
        /// Copies <paramref name="areaPoints"/> of the window's panel into a <paramref name="size"/> texture
        /// (made or resized as needed). Reads the panel's own pixels, so covering windows don't show; falls
        /// back to reading the screen if this Unity version doesn't allow that.
        /// </summary>
        public static Texture2D Grab(EditorWindow window, Rect areaPoints, Vector2Int size, Texture2D tex = null)
        {
            if (tex == null || tex.width != size.x || tex.height != size.y)
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                tex = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            }
            float ppp = EditorGUIUtility.pixelsPerPoint;
            Rect panel = PanelRect(window);
            object parent = ParentField?.GetValue(window);

            if (parent == null || GrabPixelsMethod == null)
            {
                Vector2 pos = (panel.position + areaPoints.position) * ppp;
                tex.SetPixels(InternalEditorUtility.ReadScreenPixel(pos, size.x, size.y));
                tex.Apply(false);
                return tex;
            }

            int pw = Mathf.Max(1, Mathf.RoundToInt(panel.width * ppp)), ph = Mathf.Max(1, Mathf.RoundToInt(panel.height * ppp));
            RenderTexture rt = RenderTexture.GetTemporary(pw, ph, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            try
            {
                GrabPixelsMethod.Invoke(parent, new object[] { rt, new Rect(0, 0, pw, ph) });
                RenderTexture.active = rt;
                var full = new Texture2D(pw, ph, TextureFormat.RGBA32, false, true);
                full.ReadPixels(new Rect(0, 0, pw, ph), 0, 0, false);
                _raw = full.GetPixels32();
                UnityEngine.Object.DestroyImmediate(full);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            // The grab comes back top row first; textures are bottom row first. Copy the area, flipping it,
            // and leave anything outside the panel black.
            int ax = Mathf.RoundToInt(areaPoints.x * ppp), ay = Mathf.RoundToInt(areaPoints.y * ppp);
            if (_cropped == null || _cropped.Length != size.x * size.y) _cropped = new Color32[size.x * size.y];
            else Array.Clear(_cropped, 0, _cropped.Length);
            int x0 = Mathf.Max(0, ax), x1 = Mathf.Min(pw, ax + size.x);
            for (int r = 0; r < size.y && x1 > x0; r++)
            {
                int srcRow = ay + (size.y - 1 - r);
                if (srcRow < 0 || srcRow >= ph) continue;
                Array.Copy(_raw, srcRow * pw + x0, _cropped, r * size.x + (x0 - ax), x1 - x0);
            }
            tex.SetPixels32(_cropped);
            tex.Apply(false);
            return tex;
        }

        // ------------------------------------------------------------------ recording

        private static void OnRecordingStateChanged(bool recording)
        {
            if (recording) Begin();
            else Finish(compose: true);
        }

        private static void Begin()
        {
            Finish(compose: true);
            if (!Enabled) return;
            _window = FindWindow(WindowTitle);
            if (_window == null)
            {
                Debug.LogWarning($"[EditorLabelCapture] No open window titled '{WindowTitle}'; recording without a label.");
                return;
            }

            _size = AreaPixels(_window, out _areaPoints);
            _sizePoints = _areaPoints.size;
            _frame = Grab(_window, _areaPoints, _size, _frame);
            _gameplayPath = GameplayRecorder.CurrentSegmentPath;
            _labelPath = Path.Combine(Path.GetDirectoryName(_gameplayPath), Path.GetFileNameWithoutExtension(_gameplayPath) + "_label.mp4");

            var video = new VideoTrackAttributes
            {
                frameRate = new MediaRational(FrameRate),
                width = (uint)_frame.width,
                height = (uint)_frame.height,
                includeAlpha = false,
                bitRateMode = VideoBitrateMode.High
            };
            try
            {
                _encoder = new MediaEncoder(_labelPath, video);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EditorLabelCapture] Couldn't start the label video: {ex.Message}");
                _encoder = null;
                return;
            }
            _startFrame = Time.frameCount;
            _written = 0;
            EditorApplication.update += Tick;
            Debug.Log($"[EditorLabelCapture] Recording '{WindowTitle}' ({_frame.width}x{_frame.height}) to Recordings/{Path.GetFileName(_labelPath)}");
        }

        private static void Tick()
        {
            if (_encoder == null) { EditorApplication.update -= Tick; return; }
            // Follow the recorder's game frames, not the clock: when the editor runs slower than the
            // recording's frame rate, the gameplay video is in game time and the label must match it.
            long due = (long)((Time.frameCount - _startFrame) * (FrameRate / GameplayRecorder.FrameRate)) + 1;
            if (_written >= due) return;

            if (_window == null) { Finish(compose: true); return; }
            // The video is locked to the first frame's size: keep grabbing that same area.
            _frame = Grab(_window, _areaPoints, _size, _frame);
            // When the editor stalls, repeat the frame so the label stays in time with the gameplay.
            while (_written < due)
            {
                _encoder.AddFrame(_frame);
                _written++;
            }
        }

        private static void Finish(bool compose)
        {
            EditorApplication.update -= Tick;
            if (_encoder == null) return;
            _encoder.Dispose();
            _encoder = null;
            Debug.Log($"[EditorLabelCapture] Saved Recordings/{Path.GetFileName(_labelPath)} ({_written} frames).");
            if (compose && BurnIn) ComposeWhenReady(_gameplayPath, _labelPath, _sizePoints);
        }

        // ------------------------------------------------------------------ burning in

        /// <summary>The ffmpeg to use, or null when none is found.</summary>
        public static string FindFfmpeg()
        {
            string custom = FfmpegPath;
            if (!string.IsNullOrEmpty(custom)) return File.Exists(custom) ? custom : null;
            string exe = Application.platform == RuntimePlatform.WindowsEditor ? "ffmpeg.exe" : "ffmpeg";
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim('"'), exe);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        private static void ComposeWhenReady(string gameplay, string label, Vector2 sizePoints)
        {
            // Unity Recorder finishes the MP4 a moment after stopping: wait until it can be opened.
            double deadline = EditorApplication.timeSinceStartup + 15.0, next = EditorApplication.timeSinceStartup + 1.0;
            EditorApplication.CallbackFunction wait = null;
            wait = () =>
            {
                double now = EditorApplication.timeSinceStartup;
                if (now < next) return;
                next = now + 0.5;
                if (IsReadable(gameplay)) { EditorApplication.update -= wait; Compose(gameplay, label, sizePoints); }
                else if (now > deadline)
                {
                    EditorApplication.update -= wait;
                    Debug.LogWarning($"[EditorLabelCapture] {Path.GetFileName(gameplay)} wasn't ready; label left as a separate file.");
                }
            };
            EditorApplication.update += wait;
        }

        private static bool IsReadable(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>Burns <paramref name="label"/> into a copy of <paramref name="gameplay"/> (<name>_labelled.mp4).</summary>
        public static bool Compose(string gameplay, string label, Vector2 sizePoints)
        {
            string ffmpeg = FindFfmpeg();
            if (ffmpeg == null)
            {
                Debug.Log("[EditorLabelCapture] ffmpeg not found: the label is saved as a separate video to place in the edit.");
                return false;
            }
            string output = Path.Combine(Path.GetDirectoryName(gameplay), Path.GetFileNameWithoutExtension(gameplay) + "_labelled.mp4");

            // Sizes are in output pixels: the label at LabelScale x its point size at 1080, scaled with the recording.
            float unit = GameplayRecorder.ShortSide / 1080f;
            int width = Mathf.Max(2, Mathf.RoundToInt(sizePoints.x * LabelScale * unit / 2f) * 2);
            int margin = Mathf.RoundToInt(36f * unit);
            int border = Mathf.Max(1, Mathf.RoundToInt(2f * unit));
            Corner corner = LabelCorner;
            string x = corner == Corner.TopLeft || corner == Corner.BottomLeft ? margin.ToString() : $"W-w-{margin}";
            string y = corner == Corner.TopLeft || corner == Corner.TopRight ? margin.ToString() : $"H-h-{margin}";
            string filter = $"[1:v]scale={width}:-2:flags=lanczos,pad=iw+{border * 2}:ih+{border * 2}:{border}:{border}:color=0x111114[l];" +
                            $"[0:v][l]overlay={x}:{y}:eof_action=pass,format=yuv420p[v]";
            string args = $"-y -hide_banner -loglevel error -i \"{gameplay}\" -i \"{label}\" -filter_complex \"{filter}\" " +
                          $"-map \"[v]\" -map 0:a? -c:v libx264 -crf 17 -preset veryfast -c:a copy \"{output}\"";

            var info = new ProcessStartInfo(ffmpeg, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            try
            {
                var process = new Process { StartInfo = info, EnableRaisingEvents = true };
                var errors = new System.Text.StringBuilder();
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) errors.AppendLine(e.Data); };
                process.Exited += (_, __) =>
                {
                    if (process.ExitCode == 0) Debug.Log($"[EditorLabelCapture] Saved Recordings/{Path.GetFileName(output)} with the label burned in.");
                    else Debug.LogWarning($"[EditorLabelCapture] ffmpeg failed ({process.ExitCode}): {errors}");
                    process.Dispose();
                };
                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                Debug.Log($"[EditorLabelCapture] Burning the label into {Path.GetFileName(output)}...");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EditorLabelCapture] Couldn't run ffmpeg: {ex.Message}");
                return false;
            }
        }
    }
}
