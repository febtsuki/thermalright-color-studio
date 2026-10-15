# SignalRGB bridge

Validated with SignalRGB 2.5.74 and 2.5.80. Both applications must remain running; Color Studio owns the USB display and applies real CPU/GPU digit masks.

## Setup

1. Start Color Studio and select **SignalRGB 同步** under **灯光来源**.
2. Click **安装桥接插件**. It installs a matched JS/QML pair under the running SignalRGB version's `Plugins/ColorStudio` directory, or the newest complete version if SignalRGB is not running. The configured user Plugins directory is a fallback only when no standard program installation is found.
3. Completely quit and restart SignalRGB. Open the **Color Studio Bridge** third-party service. Registration is automatic; **Connect local Color Studio** can retry discovery.
4. A **Color Studio PS120 Digital** lighting device should appear. Position it in the SignalRGB layout as desired. Color Studio reports a growing synchronized frame count.

If a redirected Documents/OneDrive path containing non-ASCII characters causes a blank service icon or a service page that cannot reopen, an ASCII plugin path was successful in the validated setup. Copy the two source assets to `<SignalRGB installation>/Signal-x64/Plugins/ColorStudio/` as `ColorStudio_SignalRGB_Bridge.js` and `ColorStudio_SignalRGB_Bridge.qml`, disable only the old pair by renaming them to `.disabled`, and restart SignalRGB. The source files are named `Assets/SignalRGBBridge.js` and `.qml`. Program-directory access may require administrator rights. SignalRGB updates can replace this installation directory; 0.3.6 can recover an existing managed program-directory installation as described below.

The option **同步中断两秒后回到独立配色** enables fallback to the saved independent effect. With fallback disabled, missing synchronization data produces a black frame. Restarting Color Studio refreshes the receiver session without announcing a second output device.

## Recovery after SignalRGB updates (0.3.6)

SignalRGB uses versioned installation directories, so custom plugins in `app-2.5.74` are not loaded by `app-2.5.80`. The installer now prefers the actual running version rather than an old directory that already contains the bridge. Version fallback uses numeric ordering and requires a complete SignalRgb.exe installation.

With **SignalRGB 更新后自动恢复已安装的桥接** enabled, Color Studio checks at startup and every 30 seconds while synchronization is enabled. It only repairs a previously installed, identifiable Color Studio program-directory bridge; it does not silently perform an initial installation. Only its two JS/QML files are managed, unchanged files are not rewritten, and replaced files are backed up. Existing `.disabled` markers are respected, including crash rollback. The option can be disabled in the UI.

If recovery happens after SignalRGB has scanned its plugins, restart SignalRGB to load the restored pair. Color Studio displays a notice when it restores files. This does not automatically restart SignalRGB or migrate its other plugins, Documents location or lighting settings.

## Smoothing

**空间平滑 · 高斯加权**, 0–100%, blends the 93 received colors using Gaussian weights based on segment-center distances. Zero is exact passthrough. Larger values soften spatial transitions and reduce local contrast. It is a sparse-sample filter, not a full-canvas blur. It cannot recover unsampled detail or make a gradient within an individual segment.

Filtering precedes numeric masks and brightness, so unlit digit segments do not artificially darken adjacent samples. It does not add temporal interpolation. Independent effects and fallback effects are unaffected.

## Color grading (0.3.5)

The **同步调色** card provides exposure compensation (-3 to +3 EV), brightness gain (0–200%), saturation (0–200%) and contrast (0–200%). Default values are 0 EV and 100% for the remaining controls. These defaults, or disabling grading, preserve input colors exactly. The reset button restores these values and enables grading without changing smoothing.

Processing order: received colors → spatial smoothing → exposure → saturation → contrast → brightness gain → channel clipping → numeric mask → overall display brightness. Exposure uses sRGB transfer functions to apply `2^EV` in linear light; the remaining controls operate on the encoded RGB values. This is a practical color adjustment model, not a calibration of the panel's LEDs.

Saturation mixes channels around a weighted grayscale value. Contrast pivots encoded channels around 0.5. Brightness gain scales the resulting RGB channels. Values are clipped to the existing RGB output range, so excessive gains can flatten highlights into white. Exact black input remains off, including at reduced contrast. Global brightness and the existing hardware channel scaling remain in place.

Settings participate in save/load, presets and undo/redo. Grading affects fresh SignalRGB frames only; independent and fallback effects are unchanged. No plugin update is needed for this feature.

## Local protocol

Discovery: HTTP GET `127.0.0.1:18742/info` returns protocol version 1, 93 LEDs, UDP port 18743 and a receiver-session token. GET `/status` reports received/rejected counts and freshness without exposing that token.

Color packet: 304 bytes, `TCSR` magic, version byte, 16-byte token, little-endian uint32 sequence, then 93 RGB triplets. Only loopback packets are accepted. Wrong token, length or version and duplicate/out-of-order fresh packets are rejected. The token distinguishes sessions; this is not protection against other processes running as the same local user, which can query the discovery endpoint.

The plugin never opens the USB device. It samples SignalRGB at the 93 mapped points and sends one packet per render call. Real hardware output rate is separately constrained by the USB protocol.
