# iPhone Screen Recovery: AirPlay and Wired HDMI

## Purpose

SoftBox provides two independent ways to view an iPhone display on Windows:

1. **AirPlay receiver** — network-based viewing. SoftBox can start the configured Windows receiver automatically at application startup and monitor its process, but it cannot silently start an iPhone's Screen Mirroring session.
2. **HDMI capture** — a wired video path for cases where a damaged touch panel makes opening Control Center impossible. It uses a compatible Apple AV video-output adapter, an HDMI cable, a UVC HDMI capture card, and an operator-supplied FFmpeg build with DirectShow support.

These paths are not interchangeable. The HDMI viewer is a real capture pipeline; it does not generate placeholder frames. A blank preview means no valid frame reached the capture process.

## Physical signal chain

**iPhone → matching Apple AV adapter → HDMI cable → UVC HDMI capture card → Windows USB port → FFmpeg DirectShow → SoftBox preview**

Connect the adapter to the iPhone, HDMI from the adapter to the capture card, and the capture card to Windows. In SoftBox open **37 — iPhone & iPad Center → Wired HDMI Capture**, select **ffmpeg.exe**, scan the DirectShow camera list, choose the capture card, then start capture.

The capture card must appear in Windows as a video-capture device. A simple USB charging/data cable from iPhone to Windows does not carry the iPhone screen as a webcam feed.

## Connector families and compatibility

| iPhone connector | Required video path | Important limitation |
|---|---|---|
| 30-pin (legacy) | A compatible 30-pin Digital AV/video adapter, if that exact iPhone model supports video output, then HDMI | Very old models and accessory combinations must be verified individually; do not assume every 30-pin device outputs video. |
| Lightning | A compatible Lightning Digital AV Adapter, then HDMI | Use a genuine or certified adapter. Some apps/content may restrict external output or require HDCP. |
| USB-C with DisplayPort video output | USB-C Digital AV/HDMI adapter or cable, then HDMI | External display support is model-specific, not guaranteed by the USB-C connector alone. |
| USB-C without DisplayPort video output | AirPlay where the phone can initiate it, or a separately supported OEM-approved workflow | A passive USB-C-to-HDMI adapter cannot add a video-output capability the phone does not expose. |

Apple's current USB-C support article lists iPhone 15 models, iPhone 16 models except iPhone 16e, iPhone 17 models except iPhone 17e, and iPhone 18 Pro models and iPhone Duo as supporting external displays through USB-C. The article also states that other USB-C iPhone models may use AirPlay instead. Re-check Apple's support page when adding newer models because this compatibility list can change: https://support.apple.com/en-gb/105099.

Apple's adapter guidance says the adapter must match the device connector (USB-C, Lightning or 30-pin): https://support.apple.com/en-gb/108399.

## Damaged screen and locked-device boundaries

- If the display is broken but the iPhone still outputs video, HDMI capture may show the lock screen or current output without opening Control Center.
- If only touch is broken, the external image may still appear. SoftBox does **not** send touchscreen events or passcodes to iOS.
- If the iPhone is powered off, has a damaged logic board/port, blocks video output, or does not support wired video output, HDMI cannot create a picture.
- A capture card does not unlock the iPhone, remove Activation Lock, bypass a passcode, or authorize a computer.
- AirPlay requires the iPhone owner to initiate/authorize Screen Mirroring on the device. Windows auto-start only prepares the receiver.

## FFmpeg and device discovery

SoftBox does not bundle or silently download FFmpeg or third-party receiver binaries. The operator chooses a local **ffmpeg.exe** built with the DirectShow input. SoftBox enumerates real Windows DirectShow video devices, starts the selected capture input, parses MJPEG frames from FFmpeg stdout, and logs stderr/process failures. No frames are invented.

If no capture card appears:
- Confirm it is connected directly to a working USB port and visible in Windows Device Manager under Cameras or Imaging devices.
- Confirm the capture card is UVC/DirectShow compatible.
- Select an FFmpeg build that includes DirectShow support.
- Close other programs that exclusively hold the capture device.
- Re-scan devices and inspect the recorded FFmpeg output.

If a device appears but no image arrives, check the iPhone AV adapter, cable, HDMI input/source, capture-card input resolution and the phone's actual video-output capability.

## Auto-start AirPlay

When the user saves a valid **airplay-windows.exe** path and leaves **Auto-start AirPlay receiver** enabled, SoftBox starts the Windows receiver on application startup. This does not claim that an iPhone is streaming. The iPhone must still initiate AirPlay itself. If the iPhone's touch panel is unusable, use a compatible wired HDMI output path when that model supports it.

## Verification status

Automated tests cover settings persistence, preservation of saved tool paths, and DirectShow device-name parsing. CI can verify compilation and unit tests. Physical verification still requires the actual iPhone model, matching AV adapter, HDMI cable, UVC capture card, Windows host, and FFmpeg build. Do not mark a hardware combination as physically verified solely because CI passes.
