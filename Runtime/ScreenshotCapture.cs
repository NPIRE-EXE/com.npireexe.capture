/*
 * ====================================================================================
 * ScreenshotCapture.cs
 * Author: Nathanael Hondi
 * Copyright: © 2026 NPIRE EXE. Licensed under the MIT License.
 * Description: Saves PNG screenshots of the Game View, with or without UI canvases.
 *              Editor output goes to <ProjectRoot>/Screenshots/, outside Assets so
 *              captures are never imported. Player builds write to
 *              <persistentDataPath>/Screenshots/.
 * ====================================================================================
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NPIREEXE.Capture
{
    public static class ScreenshotCapture
    {
        public const string FolderName = "Screenshots";

        public static string OutputFolder =>
#if UNITY_EDITOR
            Path.Combine(Path.GetDirectoryName(Application.dataPath), FolderName);
#else
            Path.Combine(Application.persistentDataPath, FolderName);
#endif

        /// <summary>Captures the Game View at the end of the current frame. Returns the file path.</summary>
        public static string Take(int scale = 1) => Capture("Screenshot", scale);

        /// <summary>Hides every enabled Canvas for the captured frame, then restores them.</summary>
        public static string TakeWithoutUI(int scale = 1)
        {
            var hidden = new List<Canvas>();
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.enabled)
                    continue;
                canvas.enabled = false;
                hidden.Add(canvas);
            }

            string path = Capture("NoUI_Screenshot", scale);
            RestoreAfterCapture(hidden);
            return path;
        }

        private static string Capture(string prefix, int scale)
        {
            string folder = OutputFolder;
            Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, $"{prefix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.png");
            ScreenCapture.CaptureScreenshot(path, scale);
            Debug.Log($"[ScreenshotCapture] Saved: {path}");
            return path;
        }

        // CaptureScreenshot reads the frame at end of frame, so canvases must stay hidden until the next one.
        private static void RestoreAfterCapture(List<Canvas> canvases)
        {
#if UNITY_EDITOR
            EditorApplication.delayCall += () => Enable(canvases);
#else
            CaptureRunner.Run(RestoreNextFrame(canvases));
#endif
        }

        private static IEnumerator RestoreNextFrame(List<Canvas> canvases)
        {
            yield return null;
            Enable(canvases);
        }

        private static void Enable(List<Canvas> canvases)
        {
            foreach (Canvas canvas in canvases)
                if (canvas != null)
                    canvas.enabled = true;
        }

        private sealed class CaptureRunner : MonoBehaviour
        {
            private static CaptureRunner instance;

            public static void Run(IEnumerator routine)
            {
                if (instance == null)
                {
                    var go = new GameObject(nameof(CaptureRunner)) { hideFlags = HideFlags.HideAndDontSave };
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<CaptureRunner>();
                }
                instance.StartCoroutine(routine);
            }
        }
    }
}
