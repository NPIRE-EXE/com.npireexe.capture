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
            Vector2Int size = OutputResolution();
            movieSettings.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = size.x,
                OutputHeight = size.y
            };
            movieSettings.AudioInputSettings.PreserveAudio = true;
            movieSettings.OutputFile = Path.Combine("Recordings", outputName);

            var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            controllerSettings.AddRecorderSettings(movieSettings);
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRate = 60f;

            controller = new RecorderController(controllerSettings);
            controller.PrepareRecording();
            if (!controller.StartRecording())
            {
                Debug.LogError("[GameplayRecorder] Recording failed to start; see the Recorder error above.");
                controller = null;
                segment--;
                if (newSession) sessionName = null;
                return;
            }
            RecordingStateChanged?.Invoke(true);

            Debug.Log($"[GameplayRecorder] Recording started at {size.x}x{size.y}: Recordings/{outputName}.mp4");
        }

        /// <summary>
        /// 1080 on the short side in the Game view's orientation, long side keeping its aspect.
        /// MP4 rejects odd dimensions, which a free-aspect Game view often has.
        /// </summary>
        private static Vector2Int OutputResolution()
        {
            PlayModeWindow.GetRenderingResolution(out uint width, out uint height);
            if (width == 0 || height == 0) return new Vector2Int(1920, 1080);

            const int shortSide = 1080;
            bool portrait = height > width;
            float aspect = portrait ? (float)height / width : (float)width / height;
            int longSide = Mathf.RoundToInt(shortSide * aspect / 2f) * 2;
            return portrait ? new Vector2Int(shortSide, longSide) : new Vector2Int(longSide, shortSide);
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
