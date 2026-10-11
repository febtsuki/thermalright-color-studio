# Architecture and protocol

The application is C# / WPF on .NET 10 for Windows x64. HID enumeration uses HidSharp; sensor readings use LibreHardwareMonitor. No PWM, voltage, power-limit or firmware writes are implemented.

- `Model.cs`: settings, palettes and spatial/animated effects.
- `PanelLayout.cs`: 93 logical rectangles, numeric masks and LF8 wire mapping, adapted under GPL-3.0 from thermalright-trcc-linux at commit `4d3fdc5b1ee2ca149fea6471a8c020db12067a30`.
- `HidService.cs`: identity validation, continuous frame output and report encoding.
- `Sensors.cs`: read-only sensor sampling and optional explicit mappings.
- `SignalBridge.cs`: loopback HTTP discovery, UDP session validation and masks.
- `SignalColorSmoothing.cs`: cached Gaussian spatial weights.
- `ColorPicker.cs`: custom HSV wheel and saturation/value square.

## Validated LF8 transport

USB VID/PID `0416:8001`; input/output reports are 65 bytes, comprising report ID 0 and 64 payload bytes. Handshake validates `PM=49 / SUB=96` before enabling LF8 output. Shared IDs alone are not used as a layout fallback.

A frame has a 20-byte header and 279 RGB payload bytes. Header command byte at offset 12 is 2; payload length at offsets 16–17 is little-endian 279. The logical colors are reordered using `WireMap` and channels scaled by 0.4. The 299-byte frame is split across five HID reports, with zero padding and a frame-end cooldown. Exact handling is in HidService.cs.

Output must be continuous on the validated panel; static colors are periodically resent. Missing, stale, non-finite or overflowing numeric readings are blanked. Identity caching is limited to a verified matching device path and descriptor in the same boot session.

The protocol is undocumented by the device manufacturer here. Only the stated device and layout have been validated; unknown layouts are rejected.
