/*
 * ====================================================================================
 * RecordingIndicatorOverlay.cs
 * Author: Nathanael Hondi
 * Copyright: © 2026 NPIRE EXE. Licensed under the MIT License.
 * Description: Scene View overlay showing a red "● REC" indicator while GameplayRecorder
 *              is actively recording (gray "○ Idle" otherwise). Purely visual — it's editor
 *              chrome, not part of the captured Game View footage, so it never shows up in
 *              the recording itself.
 *              Targets SceneView, not GameView — GameView (and its base PlayModeView) are
 *              both internal Unity classes, and Overlay attribute arguments must be a
 *              compile-time typeof(...), so there's no way to target them from outside
 *              Unity's own assembly. The Recording Control window covers the Game View case.
 *              First-time setup: open the Scene View, click the Overlays toggle in its
 *              toolbar (the small icon on the top-right), and enable "Recording Indicator" —
 *              Unity only auto-shows a newly-added overlay on a fresh layout, so an existing
 *              layout needs it switched on once.
 *              Performance: no per-frame polling — it only repaints in response to
 *              GameplayRecorder.RecordingStateChanged, i.e. exactly on start/stop.
 * ====================================================================================
 */
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

namespace NPIREEXE.Capture.Editor
{
    [Overlay(typeof(SceneView), "Recording Indicator", defaultDisplay: true)]
    public class RecordingIndicatorOverlay : IMGUIOverlay
    {
        private GUIStyle _style;

        public override void OnCreated()
        {
            GameplayRecorder.RecordingStateChanged += OnRecordingStateChanged;
        }

        public override void OnWillBeDestroyed()
        {
            GameplayRecorder.RecordingStateChanged -= OnRecordingStateChanged;
        }

        private void OnRecordingStateChanged(bool recording) => SceneView.RepaintAll();

        public override void OnGUI()
        {
            if (_style == null)
                _style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };

            bool recording = GameplayRecorder.IsRecording;
            _style.normal.textColor = recording ? Color.red : Color.gray;
            GUILayout.Label(recording ? "● REC" : "○ Idle", _style);
        }
    }
}
