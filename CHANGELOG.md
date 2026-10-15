# Changelog

## 0.3.6 — SignalRGB update recovery

- Fixed plugin installation targeting stale SignalRGB version directories.
- Prefer the running version, then the highest complete installed version.
- Added optional automatic recovery for existing managed installations after host updates, respecting disabled files and avoiding needless rewrites.
- Validated bridge loading and live synchronization with SignalRGB 2.5.80 / Qt 6.12.0.
- Added 8 path and recovery checks, bringing the total to 64.

## 0.3.5 — SignalRGB color grading

- Added exposure compensation, brightness gain, saturation and contrast after spatial smoothing.
- Added grading bypass and reset, with neutral migration defaults and preserved black/off regions.
- Preview and hardware output share the same processing pipeline; standalone effects are unchanged.
- Added 16 behavioral checks, bringing the total to 56.

## 0.3.4 — First public preview

- Custom palettes, spatial effects and animations for the LF8 93-segment panel.
- CPU/GPU readings, automatic connection, rotation and tray operation.
- Built-in HSV color picker and light/dark themes.
- SignalRGB loopback bridge with controller refresh without duplicate output registration.
- Adjustable spatial Gaussian smoothing, applied before numeric masks and brightness.
- Portable single EXE and optional sign-in startup task.
- 40 automated checks, UI rendering checks and documented hardware validation.
