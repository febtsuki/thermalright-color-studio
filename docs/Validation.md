# Validation scope

## Automated

56 self-test checks cover effect geometry and loops, masks and overflow, HID encoding, color conversion, settings migration, the SignalRGB packet format and Gaussian spatial filtering. Spatial tests check constant colors, zero-strength passthrough, input immutability, softening a local discontinuity, intermediate colors at a boundary, filtering before masks/brightness, disabled output and configuration compatibility.

WPF offscreen checks exercise page navigation, palette operations, undo/redo, light/dark themes and HSV picker rendering/hit testing. An optional Qt 6.8.1 harness imports the actual JS module and QML with mocked SignalRGB APIs, validates controller announcement, token refresh without re-announcement, 93 LED positions and 304-byte packets.

The 0.3.5 checks additionally cover neutral and bypass equivalence, exposure response, brightness gain, grayscale and saturation behavior, contrast, intentional black preservation, zero gain, clipping, input immutability, processing order, masks, settings migration and numeric validation.

## Hardware observations

On the validated PS120 DIGITAL / LF8 panel, a user visually confirmed correctly mapped red/green/blue/magenta/cyan regions and fixed test digits. Flowing gradients, live CPU/GPU numbers and source rotation were then confirmed. Automatic connection and ongoing output while hidden in the tray were verified.

With SignalRGB 2.5.74, global effects, real numeric readings and source rotation were confirmed. Restarting the receiver while SignalRGB remained running restored synchronization without a persistent duplicate stream. A five-second sample observed 151 received frames and zero additional rejects after initial old-session packets were rejected.

For 0.3.4, the user compared 0% and 40% spatial smoothing and confirmed softer transitions with normal display behavior. A separate five-second sample observed 149 received frames and zero additional rejects. The sign-in task was registered and manually triggered successfully; actual boot/login behavior still depends on the local environment.

For 0.3.5, the user confirmed exposure/saturation/contrast adjustments took effect with normal display behavior. A five-second live sample observed 150 received frames with zero additional rejects. All 56 local checks and light/dark WPF rendering checks passed.

## Not yet established

Universal device compatibility, every effect under every sensor condition, long-term stability, full disconnect/sleep recovery, precise firmware blanking timeout, and behavior across SignalRGB versions. Automated checks do not establish these hardware properties. Screenshots in this repository use simulated readings.
