# Changelog

Copyright © 2026 NPIRE EXE. Licensed under the MIT License.

All notable changes to this package are documented here. Versions follow [Semantic Versioning](https://semver.org).

## [1.0.0] - 2026-10-13

### Added
- `ScreenshotCapture` runtime API: `Take()` and `TakeWithoutUI()`.
- Menu items: Take Screenshot, Take Screenshot (No UI) (Ctrl+Shift+F12), Open Screenshots Folder.
- `GameplayRecorder`: Play Mode MP4 recording via Unity Recorder with pause/resume segments
  (Ctrl+Shift+F10 toggle, Ctrl+Shift+F11 pause/resume).
- Recording Control window and Scene View "Recording Indicator" overlay.

### Changed
- Editor screenshots now save to `<ProjectRoot>/Screenshots/` instead of `Assets/Screenshots/`,
  so captures are no longer imported as assets.
