/*
 * ====================================================================================
 * ScreenshotMenu.cs
 * Author: Nathanael Hondi
 * Copyright: © 2026 NPIRE EXE. Licensed under the MIT License.
 * Description: Editor menu items and shortcuts for ScreenshotCapture.
 * ====================================================================================
 */
using System.IO;
using UnityEditor;

namespace NPIREEXE.Capture.Editor
{
    public static class ScreenshotMenu
    {
        [MenuItem("NPIRE EXE/Tools/Capture/Take Screenshot")]
        public static void TakeScreenshot() => ScreenshotCapture.Take();

        [MenuItem("NPIRE EXE/Tools/Capture/Take Screenshot (No UI) %#_F12")]
        public static void TakeScreenshotWithoutUI() => ScreenshotCapture.TakeWithoutUI();

        [MenuItem("NPIRE EXE/Tools/Capture/Open Screenshots Folder")]
        public static void OpenScreenshotsFolder()
        {
            Directory.CreateDirectory(ScreenshotCapture.OutputFolder);
            EditorUtility.RevealInFinder(ScreenshotCapture.OutputFolder);
        }
    }
}
