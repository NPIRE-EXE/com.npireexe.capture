# NPIRE EXE Capture

Copyright © 2026 NPIRE EXE. Licensed under the MIT License, see [LICENSE.md](LICENSE.md).

Screenshot and gameplay video capture for the Unity Editor.

- **Screenshots** of the Game View, with or without UI canvases.
- **Gameplay recording** to MP4 in Play Mode, with pause/resume, built on Unity Recorder.
- **Recording resolution** from 720 to 2160 (4K) on the short side.
- **Editor label**: records the real Inspector (or any editor window) alongside gameplay and burns it
  into a corner of the video, so viewers see the actual sliders and values change.
- **Recording Control** window and a Scene View **REC indicator**.

Requires Unity 6000.0 or newer. `com.unity.recorder` is installed automatically as a dependency.

## Install

**Package Manager (recommended).** Add to `Packages/manifest.json` under `dependencies`:

```json
"com.npireexe.capture": "https://github.com/NPIRE-EXE/com.npireexe.capture.git#v1.1.0"
```

Unity Recorder is installed automatically.

**.unitypackage.** Download `com.npireexe.capture-<version>.unitypackage` from the
[latest release](https://github.com/NPIRE-EXE/com.npireexe.capture/releases/latest) and open it in Unity.
It imports to `Assets/NPIRE EXE/Capture/`. Install **Recorder** (`com.unity.recorder`) from Package Manager first.
Don't use both install methods in one project.

## Update

Package Manager: change `#v1.1.0` at the end of the URL to the new version and return to Unity.
.unitypackage: delete `Assets/NPIRE EXE/Capture/` and import the new one.
See [CHANGELOG.md](CHANGELOG.md) for what changed.

## Usage

### Menu and shortcuts

All items are under **NPIRE EXE > Tools > Capture**.

| Item | Shortcut |
|---|---|
| Take Screenshot | |
| Take Screenshot (No UI) | Ctrl+Shift+F12 |
| Open Screenshots Folder | |
| Toggle Gameplay Recording | Ctrl+Shift+F10 |
| Pause-Resume Gameplay Recording | Ctrl+Shift+F11 |
| Recording Control (window) | |
| Open Recordings Folder | |

Recording only works in Play Mode. Each pause starts a new segment file:
`Recordings/Gameplay_<timestamp>_part01.mp4`, `_part02.mp4`, ...

**Resolution.** The Recording Control window's **Short side** sets the output size in the Game
View's orientation: 1080 gives 1920x1080 (or 1080x1920 in portrait), 2160 gives 4K. Record at 2160
when you want room to crop, zoom or pan in the edit. The choice is saved per machine and applies
from the next segment. From code: `GameplayRecorder.ShortSide = 2160;`.

### Editor label

Shows the real Unity controls on the video: drag a slider in the Inspector during a recording and
the label in the corner shows it moving.

1. Open **Recording Control** and the **Editor label** section. Turn on **Record label**.
2. **Window**: the editor window to show (Inspector by default; any open window works, including
   custom tools).
3. **Refresh preview**, then drag on the preview to pick the area, for example one component.
   **Whole window** clears it.
4. Pick the **Corner** and **Size** (1 = the window's size at 1080p, scaled with the recording).
5. Record as usual. Each segment also writes:
   - `<name>_label.mp4`: the label on its own, the same length as the gameplay clip.
   - `<name>_labelled.mp4`: the gameplay with the label burned in (needs `ffmpeg` on PATH; turn off
     **Burn into video** to skip it). The original recording is left as it was.

The label is read from the window's own pixels, so other windows covering it don't matter. In a
docked group, keep the window's tab selected while recording: only the selected tab is drawn. The
label follows game frames like the gameplay recording, so the two stay in sync even when the editor
runs below 60 fps. Settings are saved per machine.

The REC indicator overlay lives in the Scene View. On an existing layout, enable it once from the
Scene View Overlays menu ("Recording Indicator").

### From code

```csharp
using NPIREEXE.Capture;

ScreenshotCapture.Take();          // returns the saved file path
ScreenshotCapture.TakeWithoutUI(); // hides enabled canvases for one frame
```

`ScreenshotCapture` is in the runtime assembly, so game code can call it. In player builds files go to
`<persistentDataPath>/Screenshots/`.

## Output folders

| Output | Editor location |
|---|---|
| Screenshots | `<ProjectRoot>/Screenshots/` |
| Recordings | `<ProjectRoot>/Recordings/` |

Both are outside `Assets/`. Add them to the project's `.gitignore`:

```
/Screenshots/
/Recordings/
```

## Assemblies

| Assembly | Platforms | Contents |
|---|---|---|
| `NPIREEXE.Capture` | All | `ScreenshotCapture` |
| `NPIREEXE.Capture.Editor` | Editor | Menus, `GameplayRecorder`, `EditorLabelCapture`, window, overlay |
