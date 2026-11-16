/*
 * ====================================================================================
 * RecordingControlWindow.cs
 * Author: Nathanael Hondi
 * Copyright: © 2026 NPIRE EXE. Licensed under the MIT License.
 * Description: Dockable Editor window for GameplayRecorder — Start/Stop, Pause/Resume,
 *              a live "● REC" / "○ Idle" indicator, and a shortcut to the Recordings
 *              folder. Dock it next to the Game View for an always-visible recording
 *              status instead of relying on the keyboard shortcuts alone.
 * ====================================================================================
 */
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NPIREEXE.Capture.Editor
{
    public class RecordingControlWindow : EditorWindow
    {
        private GUIStyle _indicatorStyle;
        private Vector2 _scroll;
        private bool _labelFoldout = true;
        private Texture2D _preview;
        private Rect _previewScreen;
        private Vector2? _dragStart;

        [MenuItem("NPIRE EXE/Tools/Capture/Recording Control")]
        public static void ShowWindow()
        {
            RecordingControlWindow window = GetWindow<RecordingControlWindow>("Recording");
            window.minSize = new Vector2(220, 165);
        }

        private void OnEnable()
        {
            GameplayRecorder.RecordingStateChanged += OnStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            if (_preview != null) DestroyImmediate(_preview);
            GameplayRecorder.RecordingStateChanged -= OnStateChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnStateChanged(bool recording) => Repaint();
        private void OnPlayModeStateChanged(PlayModeStateChange state) => Repaint();

        private void OnGUI()
        {
            if (_indicatorStyle == null)
                _indicatorStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleCenter };

            bool recording = GameplayRecorder.IsRecording;
            _indicatorStyle.normal.textColor = recording ? Color.red : Color.gray;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Space(6);
            GUILayout.Label(recording ? "● REC" : "○ Idle", _indicatorStyle);
            GUILayout.Space(8);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button(recording ? "Stop Recording" : "Start Recording", GUILayout.Height(28)))
                    GameplayRecorder.ToggleRecording();

                bool canPauseResume = recording || GameplayRecorder.HasActiveSession;
                using (new EditorGUI.DisabledScope(!canPauseResume))
                {
                    if (GUILayout.Button(recording ? "Pause" : "Resume", GUILayout.Height(24)))
                        GameplayRecorder.TogglePause();
                }
            }

            GUILayout.Space(8);
            int[] sides = GameplayRecorder.ShortSideOptions;
            string[] labels = System.Array.ConvertAll(sides, v => v == 2160 ? "2160 (4K)" : v.ToString());
            int current = System.Array.IndexOf(sides, GameplayRecorder.ShortSide);
            int picked = EditorGUILayout.Popup(new GUIContent("Short side", "Output height (width in portrait). 2160 leaves room to crop or zoom in the edit. Applies from the next recording."), current < 0 ? 1 : current, labels);
            if (picked != current)
                GameplayRecorder.ShortSide = sides[picked];

            GUILayout.Space(4);
            if (GUILayout.Button("Open Recordings Folder"))
                GameplayRecorder.OpenRecordingsFolder();

            if (!Application.isPlaying)
            {
                GUILayout.Space(6);
                EditorGUILayout.HelpBox("Enter Play Mode to record.", MessageType.Info);
            }

            GUILayout.Space(8);
            DrawEditorLabel(recording);
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ editor label

        private void DrawEditorLabel(bool recording)
        {
            _labelFoldout = EditorGUILayout.Foldout(_labelFoldout, "Editor label", true);
            if (!_labelFoldout) return;

            using (new EditorGUI.DisabledScope(recording))
            {
                EditorLabelCapture.Enabled = EditorGUILayout.Toggle(new GUIContent("Record label",
                    "Also record part of an editor window (the real Inspector controls) while recording gameplay."), EditorLabelCapture.Enabled);
                if (!EditorLabelCapture.Enabled) return;

                string[] titles = EditorLabelCapture.OpenWindowTitles();
                int index = System.Array.IndexOf(titles, EditorLabelCapture.WindowTitle);
                bool missing = index < 0;
                if (missing)
                {
                    titles = new[] { EditorLabelCapture.WindowTitle + " (not open)" }.Concat(titles).ToArray();
                    index = 0;
                }
                int picked = EditorGUILayout.Popup("Window", index, titles);
                if (picked != index && !(missing && picked == 0))
                {
                    EditorLabelCapture.WindowTitle = titles[picked];
                    EditorLabelCapture.Area = Rect.zero;
                    RefreshPreview();
                }

                EditorLabelCapture.LabelCorner = (EditorLabelCapture.Corner)EditorGUILayout.EnumPopup("Corner", EditorLabelCapture.LabelCorner);
                EditorLabelCapture.LabelScale = EditorGUILayout.Slider(new GUIContent("Size",
                    "Label size on the video. 1 = the window's size at 1080p, scaled with the recording."), EditorLabelCapture.LabelScale, 0.25f, 4f);

                EditorLabelCapture.BurnIn = EditorGUILayout.Toggle(new GUIContent("Burn into video",
                    "After each recording, write <name>_labelled.mp4 with the label in the corner (needs ffmpeg)."), EditorLabelCapture.BurnIn);
                if (EditorLabelCapture.BurnIn && EditorLabelCapture.FindFfmpeg() == null)
                    EditorGUILayout.HelpBox("ffmpeg not found on PATH. The label is still saved as <name>_label.mp4.", MessageType.Warning);

                GUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Refresh preview")) RefreshPreview();
                    if (GUILayout.Button("Whole window")) { EditorLabelCapture.Area = Rect.zero; RefreshPreview(); }
                }
                DrawPreview();
                EditorGUILayout.HelpBox("Drag on the preview to pick the area. In a docked group, keep that window's tab selected " +
                    "while recording: only the selected tab is drawn.", MessageType.None);
            }
            if (EditorLabelCapture.IsCapturing)
                EditorGUILayout.HelpBox("Recording label.", MessageType.Info);
        }

        private void RefreshPreview()
        {
            EditorWindow window = EditorLabelCapture.FindWindow(EditorLabelCapture.WindowTitle);
            if (window == null) return;
            _previewScreen = EditorLabelCapture.PanelRect(window);
            float ppp = EditorGUIUtility.pixelsPerPoint;
            var size = new Vector2Int(Mathf.Max(2, Mathf.RoundToInt(_previewScreen.width * ppp) / 2 * 2),
                                      Mathf.Max(2, Mathf.RoundToInt(_previewScreen.height * ppp) / 2 * 2));
            _preview = EditorLabelCapture.Grab(window, new Rect(0f, 0f, _previewScreen.width, _previewScreen.height), size, _preview);
            Repaint();
        }

        private void DrawPreview()
        {
            if (_preview == null)
            {
                EditorGUILayout.LabelField("Press Refresh preview to pick an area.", EditorStyles.miniLabel);
                return;
            }
            float width = Mathf.Max(60f, EditorGUIUtility.currentViewWidth - 24f);
            float height = Mathf.Min(260f, width * _preview.height / _preview.width);
            width = height * _preview.width / _preview.height;
            Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(rect, _preview, ScaleMode.StretchToFill);

            // Preview pixels -> window points.
            float toPoints = _previewScreen.width / rect.width;
            Rect area = EditorLabelCapture.Area;
            if (area.width >= 8f && area.height >= 8f)
            {
                var shown = new Rect(rect.x + area.x / toPoints, rect.y + area.y / toPoints, area.width / toPoints, area.height / toPoints);
                DrawOutline(shown, new Color(1f, 0.76f, 0.24f));
            }

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                _dragStart = e.mousePosition;
                e.Use();
            }
            else if (_dragStart.HasValue && (e.type == EventType.MouseDrag || e.type == EventType.MouseUp))
            {
                Vector2 a = _dragStart.Value, b = e.mousePosition;
                var sel = Rect.MinMaxRect(Mathf.Max(rect.xMin, Mathf.Min(a.x, b.x)), Mathf.Max(rect.yMin, Mathf.Min(a.y, b.y)),
                                          Mathf.Min(rect.xMax, Mathf.Max(a.x, b.x)), Mathf.Min(rect.yMax, Mathf.Max(a.y, b.y)));
                EditorLabelCapture.Area = new Rect((sel.x - rect.x) * toPoints, (sel.y - rect.y) * toPoints, sel.width * toPoints, sel.height * toPoints);
                if (e.type == EventType.MouseUp) _dragStart = null;
                e.Use();
                Repaint();
            }
        }

        private static void DrawOutline(Rect r, Color color)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 2f), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2f, r.width, 2f), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 2f, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - 2f, r.y, 2f, r.height), color);
        }
    }
}
