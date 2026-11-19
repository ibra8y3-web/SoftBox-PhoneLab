# Remote test control

PhoneLab Windows can start supported tests on the phone over the existing USB-forwarded local Agent. The Android Agent binds only to 127.0.0.1:27831. Windows reaches it via `adb forward`; it has no Wi-Fi/LAN listener.

## Endpoints

- `GET /health` — local Agent health and version.
- `GET /snapshot` — device identity, telemetry, camera routes, sensors, hardware feature flags and test results.
- `POST /command` — starts one allow-listed on-phone test. Unknown commands are rejected.

## Command allow-list

| Command | Action on the phone |
|---|---|
| `display` | Ten full-screen patterns: RGB, white, black, grayscale and color patterns; the user confirms the result. |
| `touch` | Touch coverage grid; user completes the cells. |
| `speaker` | Audio beep with user confirmation. |
| `earpiece` | Voice-call audio channel beep with user confirmation. |
| `microphone` | Short microphone recording; reports Android recorder amplitude. |
| `flashlight` | Short torch pulse and user confirmation. |
| `vibration` | Short vibration and user confirmation. |
| `camera` | Lists exposed logical/physical routes, captures JPEG and records autofocus state where exposed. |
| `sensors` | Reads live sensor event samples for a short sampling window. |
| `hardware` | Enumerates Android system hardware features and Camera2 count. |
| `battery_capacity_start` / `battery_capacity_finish` | Starts and finishes a guarded fuel-gauge capacity observation. |

All tests requiring sight, hearing or physical feedback keep their confirmation dialog on the phone. The Windows app never silently declares a user-confirmed test passed.

## Safety and interpretation

- No arbitrary shell command, firmware write, partition erase, factory reset, bootloader unlock or security-lock bypass is exposed by this endpoint.
- The Agent server is stopped while the Activity is paused and restarted when resumed.
- Sensor presence and hardware-feature declarations are data-read results; they do not prove electrical or functional health.
- Camera autofocus state does not prove the sharpness of a captured image. A chart/target inspection remains separate.
- Microphone amplitude is not a calibrated frequency-response or signal-to-noise measurement.