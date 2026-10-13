# NPIRE EXE Capture

Copyright © 2026 NPIRE EXE. Licensed under the MIT License, see [LICENSE.md](LICENSE.md).

Screenshot and gameplay video capture for the Unity Editor.

- **Screenshots** of the Game View, with or without UI canvases.
- **Gameplay recording** to MP4 in Play Mode, with pause/resume, built on Unity Recorder.
- **Recording Control** window and a Scene View **REC indicator**.

Requires Unity 6000.0 or newer. `com.unity.recorder` is installed automatically as a dependency.

## Install

**Package Manager (recommended).** Add to `Packages/manifest.json` under `dependencies`:

```json
"com.npireexe.capture": "https://github.com/NPIRE-EXE/com.npireexe.capture.git#v1.0.0"
```

Unity Recorder is installed automatically.

**.unitypackage.** Download `com.npireexe.capture-<version>.unitypackage` from the
[latest release](https://github.com/NPIRE-EXE/com.npireexe.capture/releases/latest) and open it in Unity.
It imports to `Assets/NPIRE EXE/Capture/`. Install **Recorder** (`com.unity.recorder`) from Package Manager first.
Don't use both install methods in one project.

## Update

Package Manager: change `#v1.0.0` at the end of the URL to the new version and return to Unity.
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
| `NPIREEXE.Capture.Editor` | Editor | Menus, `GameplayRecorder`, window, overlay |
