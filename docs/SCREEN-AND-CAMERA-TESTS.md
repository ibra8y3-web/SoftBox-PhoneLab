# Screen, camera and battery tests

## Screen mirroring
The Windows app downloads the official scrcpy Windows archive on first use, verifies its SHA-256 against the publisher's Windows documentation, and launches a live USB mirror. This is a separate scrcpy window and does not replace the phone's physical display.

## Physical panel inspection
A normal Android screenshot is generated before the physical display panel. It cannot prove that a physical OLED/LCD pixel lights correctly. To screen for physical panel defects:
1. Use PhoneLab Agent to display a uniform red, green, blue, white or black pattern.
2. Photograph the physical panel with a fixed-focus camera under controlled exposure, angle and room lighting.
3. Use the Windows image analyzer on that camera photo.
4. Review suspected outlier pixels and regional luminance/color variation. The analyzer reports candidates, not automatic confirmed failures; moiré, reflections and exposure can create false positives.

## Individual cameras
The Agent enumerates Android Camera2 logical camera IDs, focal lengths, sensor dimensions, flash and physical camera IDs exposed by the vendor. It requests a real still JPEG from each camera route it can open, including physical routes exposed by the Camera2 multi-camera API. Camera modules hidden by vendor firmware or not exposed to third-party Camera2 apps cannot be tested by this path.

## Battery telemetry
When Android exposes them, PhoneLab reads charge counter (microampere-hours), instantaneous and average battery current (microamperes), remaining energy (nanowatt-hours), voltage, temperature and API 34+ cycle count. These are fuel-gauge reports, not a certified full-capacity or internal-resistance test. A full capacity test requires a controlled charge/discharge cycle or an external battery analyzer.

## References
- Android BatteryManager: https://developer.android.com/reference/android/os/BatteryManager
- Android multi-camera API: https://developer.android.com/media/camera/camera2/multi-camera
- Official scrcpy Windows instructions: https://github.com/Genymobile/scrcpy/blob/master/doc/windows.md


## Battery capacity observation
The Android Agent offers a start/end observation. It stores the initial fuel-gauge charge counter and state of charge locally, then estimates full-charge capacity from the measured change after at least 10 percentage points of discharge and 15 minutes. Use the phone under its normal expected workload without charging during the observation. The result is always marked for review and is not a calibrated capacity or internal-resistance measurement. Phones that do not expose a valid charge counter require an external battery analyzer.
