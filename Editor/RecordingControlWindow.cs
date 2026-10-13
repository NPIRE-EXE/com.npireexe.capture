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
using UnityEditor;
using UnityEngine;

namespace NPIREEXE.Capture.Editor
{
    public class RecordingControlWindow : EditorWindow
    {
        private GUIStyle _indicatorStyle;

        [MenuItem("NPIRE EXE/Tools/Capture/Recording Control")]
        public static void ShowWindow()
        {
            RecordingControlWindow window = GetWindow<RecordingControlWindow>("Recording");
            window.minSize = new Vector2(200, 140);
        }

        private void OnEnable()
        {
            GameplayRecorder.RecordingStateChanged += OnStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
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
            if (GUILayout.Button("Open Recordings Folder"))
                GameplayRecorder.OpenRecordingsFolder();

            if (!Application.isPlaying)
            {
                GUILayout.Space(6);
                EditorGUILayout.HelpBox("Enter Play Mode to record.", MessageType.Info);
            }
        }
    }
}
