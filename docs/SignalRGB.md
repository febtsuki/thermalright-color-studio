# SignalRGB bridge

Validated with SignalRGB 2.5.74. Both applications must remain running; Color Studio owns the USB display and applies real CPU/GPU digit masks.

## Setup

1. Start Color Studio and select **SignalRGB 同步** under **灯光来源**.
2. Click **安装桥接插件**. It installs a matched JS/QML pair to the configured SignalRGB user Plugins directory, or updates an existing ColorStudio installation under SignalRGB's program Plugins directory.
3. Completely quit and restart SignalRGB. Open the **Color Studio Bridge** third-party service. Registration is automatic; **Connect local Color Studio** can retry discovery.
4. A **Color Studio PS120 Digital** lighting device should appear. Position it in the SignalRGB layout as desired. Color Studio reports a growing synchronized frame count.

If a redirected Documents/OneDrive path containing non-ASCII characters causes a blank service icon or a service page that cannot reopen, an ASCII plugin path was successful in the validated setup. Copy the two source assets to `<SignalRGB installation>/Signal-x64/Plugins/ColorStudio/` as `ColorStudio_SignalRGB_Bridge.js` and `ColorStudio_SignalRGB_Bridge.qml`, disable only the old pair by renaming them to `.disabled`, and restart SignalRGB. The source files are named `Assets/SignalRGBBridge.js` and `.qml`. Program-directory access may require administrator rights. SignalRGB updates can replace this installation directory; check the plugin after upgrading.

The option **同步中断两秒后回到独立配色** enables fallback to the saved independent effect. With fallback disabled, missing synchronization data produces a black frame. Restarting Color Studio refreshes the receiver session without announcing a second output device.

## Smoothing

**空间平滑 · 高斯加权**, 0–100%, blends the 93 received colors using Gaussian weights based on segment-center distances. Zero is exact passthrough. Larger values soften spatial transitions and reduce local contrast. It is a sparse-sample filter, not a full-canvas blur. It cannot recover unsampled detail or make a gradient within an individual segment.

Filtering precedes numeric masks and brightness, so unlit digit segments do not artificially darken adjacent samples. It does not add temporal interpolation. Independent effects and fallback effects are unaffected.

## Local protocol

Discovery: HTTP GET `127.0.0.1:18742/info` returns protocol version 1, 93 LEDs, UDP port 18743 and a receiver-session token. GET `/status` reports received/rejected counts and freshness without exposing that token.

Color packet: 304 bytes, `TCSR` magic, version byte, 16-byte token, little-endian uint32 sequence, then 93 RGB triplets. Only loopback packets are accepted. Wrong token, length or version and duplicate/out-of-order fresh packets are rejected. The token distinguishes sessions; this is not protection against other processes running as the same local user, which can query the discovery endpoint.

The plugin never opens the USB device. It samples SignalRGB at the 93 mapped points and sends one packet per render call. Real hardware output rate is separately constrained by the USB protocol.
