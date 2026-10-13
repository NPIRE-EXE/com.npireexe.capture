/*
 * ====================================================================================
 * GameplayRecorder.cs
 * Author: Nathanael Hondi
 * Copyright: © 2026 NPIRE EXE. Licensed under the MIT License.
 * Description: Editor-only Play Mode gameplay video recorder built on Unity Recorder,
 *              controlled via keyboard shortcuts instead of the Recorder window.
 *              Output is written to <ProjectRoot>/Recordings/.
 * ====================================================================================
 */
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace NPIREEXE.Capture.Editor
{
    public static class GameplayRecorder
    {
        private static RecorderController controller;
        private static string sessionName;
        private static int segment;

        public static bool IsRecording => controller != null && controller.IsRecording();

        /// <summary>True once a session has started, even mid-pause — lets UI know whether "Resume" is meaningful.</summary>
        public static bool HasActiveSession => sessionName != null;

        /// <summary>Fires whenever recording starts or stops/pauses — lets listeners react instead of polling IsRecording every frame.</summary>
        public static event Action<bool> RecordingStateChanged;

        static GameplayRecorder()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("NPIRE EXE/Tools/Capture/Toggle Gameplay Recording %#_F10")]
        public static void ToggleRecording()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[GameplayRecorder] Enter Play Mode before recording.");
                return;
            }

            if (IsRecording)
                StopSession();
            else
                StartSegment(newSession: true);
        }

        [MenuItem("NPIRE EXE/Tools/Capture/Open Recordings Folder")]
        public static void OpenRecordingsFolder()
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Recordings");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("NPIRE EXE/Tools/Capture/Pause-Resume Gameplay Recording %#_F11")]
        public static void TogglePause()
        {
            if (!Application.isPlaying)
                return;

            if (IsRecording)
            {
                StopSegment();
                Debug.Log("[GameplayRecorder] Recording paused.");
            }
            else if (sessionName != null)
            {
                StartSegment(newSession: false);
                Debug.Log("[GameplayRecorder] Recording resumed.");
            }
            else
            {
                Debug.LogWarning("[GameplayRecorder] No recording in progress to resume.");
            }
        }

        private static void StartSegment(bool newSession)
        {
            if (newSession)
            {
                sessionName = $"Gameplay_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
                segment = 0;
            }

            segment++;
            string outputName = $"{sessionName}_part{segment:00}";

            var movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movieSettings.name = "Gameplay Movie Recorder";
            movieSettings.Enabled = true;
            movieSettings.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
            movieSettings.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = Screen.width,
                OutputHeight = Screen.height
            };
            movieSettings.AudioInputSettings.PreserveAudio = true;
            movieSettings.OutputFile = Path.Combine("Recordings", outputName);

            var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            controllerSettings.AddRecorderSettings(movieSettings);
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRate = 60f;

            controller = new RecorderController(controllerSettings);
            controller.PrepareRecording();
            controller.StartRecording();
            RecordingStateChanged?.Invoke(true);

            Debug.Log($"[GameplayRecorder] Recording started: Recordings/{outputName}.mp4");
        }

        private static void StopSegment()
        {
            controller?.StopRecording();
            RecordingStateChanged?.Invoke(false);
        }

        private static void StopSession()
        {
            StopSegment();
            Debug.Log($"[GameplayRecorder] Recording stopped ({segment} segment(s) saved under Recordings/{sessionName}_part*.mp4).");
            controller = null;
            sessionName = null;
            segment = 0;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode && IsRecording)
                StopSession();
        }
    }
}
