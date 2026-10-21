# Changelog

Copyright © 2026 NPIRE EXE. Licensed under the MIT License.

All notable changes to this package are documented here. Versions follow [Semantic Versioning](https://semver.org).

## [1.0.1] - 2026-10-22

### Fixed
- `GameplayRecorder` records at 1080 on the short side in the Game view's orientation (1080x1920
  portrait, 1920x1080 landscape at 16:9) instead of the raw Game view size, which MP4 rejected
  whenever a dimension was odd.
- A recording that fails to start is reported as an error instead of logging "Recording started"
  and raising `RecordingStateChanged(true)`.

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
