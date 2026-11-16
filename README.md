# NPIRE EXE Capture

Copyright © 2026 NPIRE EXE. Licensed under the MIT License, see [LICENSE.md](LICENSE.md).

Screenshot and gameplay video capture for the Unity Editor.

- **Screenshots** of the Game View, with or without UI canvases.
- **Gameplay recording** to MP4 in Play Mode, with pause/resume, built on Unity Recorder.
- **Recording resolution** from 720 to 2160 (4K) on the short side.
- **Value Overlay**: a card in a corner of the Game View showing live values, captured in recordings and screenshots.
- **Recording Control** window and a Scene View **REC indicator**.

Requires Unity 6000.0 or newer. `com.unity.recorder` and `com.unity.ugui` are installed automatically as dependencies.

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
| Add Value Overlay | |

Recording only works in Play Mode. Each pause starts a new segment file:
`Recordings/Gameplay_<timestamp>_part01.mp4`, `_part02.mp4`, ...

**Resolution.** The Recording Control window's **Short side** sets the output size in the Game
View's orientation: 1080 gives 1920x1080 (or 1080x1920 in portrait), 2160 gives 4K. Record at 2160
when you want room to crop, zoom or pan in the edit. The choice is saved per machine and applies
from the next segment. From code: `GameplayRecorder.ShortSide = 2160;`.

The REC indicator overlay lives in the Scene View. On an existing layout, enable it once from the
Scene View Overlays menu ("Recording Indicator").

### Value Overlay

Shows values changing on screen while you record: a slider, a shader setting, rain level, a score.

1. **NPIRE EXE > Tools > Capture > Add Value Overlay** (or add the **Value Overlay** component to any
   GameObject).
2. Add a row per value. **Source**:
   - **Member**: drag in a GameObject, then pick a field or property from the list, which covers
     every component on it (numbers, bools, enums, vectors, colours; public, `[SerializeField]` and
     `_private` fields).
   - **Material Property**: a material and one of its shader properties.
   - **Shader Global**: a global shader float by name.
3. Set **Show Bar** with **Min / Max** to draw a bar. A value flashes in the accent colour when it changes.
4. Pick the corner, size and colours. The card scales with the screen's short side, so it looks the
   same at 1080 and 4K, portrait or landscape.

The card is a Screen Space Overlay canvas that is generated at runtime and never saved into the scene.
Recordings and screenshots capture it, and **Take Screenshot (No UI)** keeps it unless **Keep In No UI
Captures** is off. Turn **Show In Edit Mode** off to see it in Play Mode only.

To show a setting changing on camera, drive it from one value to another (Play Mode):

```csharp
var overlay = Object.FindAnyObjectByType<ValueOverlay>();
overlay.Sweep("Rain", 0f, 1f, 4f);               // by row label, over 4 seconds
overlay.Sweep(0, 0f, 1f, 4f, easeInOut: true);   // by row index
overlay.StopSweeps();
```

`Sweep` writes the value (float, int, bool or enum members with a setter or a field, material
floats and ints, shader globals) and returns false if it can't. Values that only live in an editor
window (not on a scene object, material or global) can't be shown: record that window with a screen
recorder instead.

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
| `NPIREEXE.Capture` | All | `ScreenshotCapture`, `ValueOverlay` |
| `NPIREEXE.Capture.Editor` | Editor | Menus, `GameplayRecorder`, window, REC overlay, Value Overlay inspector |
