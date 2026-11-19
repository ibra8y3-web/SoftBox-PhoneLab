# SoftBox PhoneLab

**Windows smartphone diagnostics workstation with an Android USB companion.**

PhoneLab discovers authorized Android phones over ADB, reads available device identifiers, runs permitted on-device tests, and records evidence for each component. It deliberately distinguishes actual test results from information merely read from Android.

## Current implemented capabilities

### Device detection and model profiles
- ADB auto-discovery every 3 seconds.
- Reads manufacturer, marketing model, model number, product, device codename, hardware SKU, build fingerprint, board, SoC, bootloader, CPU and ABI.
- Matches a bundled curated catalog and automatically adds the Apache-2.0 community model-code mapping generated from Google's public Play supported-device list. The community catalog updates upstream daily and is cached locally by PhoneLab.
- Shows match confidence and evidence; an identity profile is not treated as a boardview file.

The curated catalog is a starter set. The additional community model-code database greatly broadens Android model-name matching, but it is not guaranteed to contain every regional variant or every phone ever sold. Community-only matches are shown as likely, not confirmed.

### 3D component workspace
- Interactive exploded smartphone schematic with component selection and test-state colors.
- Imports model-specific board-map JSON files only when they identify an exact model/codename and include HTTPS source, reviewer and verification timestamp.
- The only shipped map is a generic educational fallback. No fake IC coordinates are generated for unknown devices.

### Screen and cameras
- Launches the official scrcpy Windows client for live USB screen mirroring. The archive's SHA-256 is checked against the official scrcpy Windows installation page before extraction.
- Agent displays uniform color test patterns. A camera photograph of the physical screen can be analyzed for candidate pixel outliers and luminance non-uniformity. This is a screening aid, not a calibrated optical inspection.
- Android Camera2 enumerates logical camera IDs and exposed physical camera routes, reads lens and autofocus metadata, captures a JPEG for each route, and records the autofocus state separately. This verifies reported AF state, not chart sharpness, stabilization or optical quality.

### Battery and electrical measurements
- Reads Android fuel-gauge information when exposed: charge counter, current now/average, remaining energy, voltage, temperature and API 34+ cycle count.
- Agent start/end battery observation estimates capacity from charge-counter delta and state-of-charge drop. It requires at least 10% discharge and 15 minutes, rejects charging/hot samples, and always reports the estimate as review-only.
- Adds a serial measurement dashboard for compatible USB/bench instruments using the read-only JSON-Lines v1 protocol documented in docs/MEASUREMENT-RIG-PROTOCOL.md.
- Supports USB input, battery voltage/current, board temperature, power, named rails and optional battery capacity/internal-resistance fields when the connected analyzer provides them. It records the measurement history and exports it to CSV.
- It does not issue voltage/current setpoints or power commands. Raw measurements are data observations; they are not automatically judged healthy. Optional capacity and internal-resistance values can be ingested when a compatible external battery analyzer supplies them; readings are observations, not automatic health verdicts.

Fuel-gauge data is not a certified battery-capacity test. A real capacity estimate needs a controlled, sufficiently long charge/discharge measurement or an external battery analyzer. A USB meter cannot measure hidden PMIC rails without a properly engineered model-specific probe.

### Repair knowledge and reports
- On-demand iFixit repair guide search opens original source pages; PhoneLab does not mirror their copyrighted repair content.
- Windows Remote Test panel starts allow-listed on-phone tests over ADB-forwarded loopback; results synchronize automatically.
- Exports device identity, Agent telemetry, external meter readings, component results and test limitations in JSON.

## Repository modules

| Module | Purpose |
|---|---|
| src/SoftBox.PhoneLab.Desktop | Windows WPF app, live mirror, screen-photo analysis and measurement dashboard |
| src/SoftBox.PhoneLab.Core | Device snapshots, model profiles, parsing and diagnostic evidence mapping |
| agent | Android PhoneLab Agent, Camera2 diagnostics and local ADB-forward bridge |
| data/device-profiles | Curated starter device identity catalog |
| data/boardmaps | Verified-map format and generic fallback |
| hardware/measurement-rig | Safe external measurement integration notes |
| tests | Parser and diagnostic unit tests |

## Requirements

- Windows 10/11 x64.
- Official Android SDK Platform-Tools (adb.exe).
- Android phone with a data USB cable and USB debugging authorization.
- The Windows artifact is self-contained; the Android Agent debug APK is built separately.
- Internet access is required for the first scrcpy and TRACE Boardviewer installations, optional device-catalog updates and repair-guide search.

## Build Windows

    dotnet restore .\SoftBox.PhoneLab.sln
    dotnet test .\SoftBox.PhoneLab.sln -c Release
    dotnet publish .\src\SoftBox.PhoneLab.Desktop\SoftBox.PhoneLab.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o .\artifacts\PhoneLab-win-x64

## Build Android Agent

    cd .\agent
    gradle :app:assembleDebug
    adb install -r .\app\build\outputs\apk\debug\app-debug.apk

Open the Agent and keep it in the foreground while synchronizing tests from Windows.

## Typical workflow

1. Start PhoneLab and connect the phone over USB.
2. Enable USB debugging and approve the PC trust prompt.
3. Select the detected device. PhoneLab reads its identifiers and automatically matches the device-profile catalog.
4. Use live screen mirror, ten-pattern full-screen display check, Camera2 route capture and reported autofocus, touch, speaker, earpiece, microphone, flashlight, vibration, live sensor events and hardware-feature inventory. Start tests from the Windows Remote Test panel.
5. Use the physical display photo analyzer with a real camera photo of the screen during a uniform test pattern.
6. Connect a supported, electrically safe serial measurement fixture for USB/battery voltage/current, temperature, optional capacity/internal resistance and named rails. Export the history to CSV.
7. Review every component's evidence and export a JSON report.

## Diagnostic color contract

- Green: a named functional test passed.
- Yellow: a value or result needs review.
- Red: a named test reported failure.
- Blue: data was read; health is not proven.
- Gray: not tested or unavailable.

## Important engineering limits

- PhoneLab opens supported native BoardView files in the separate MIT-licensed TRACE Boardviewer. Its own 3D workspace still imports PhoneLab JSON placement maps; it does not decode every native BRD/BDV/FZ/BVR/GenCAD format into the embedded 3D scene. Exact board coordinates, IC positions and socket placements cannot be fabricated from an Android model string.
- The identity catalog combines curated profiles with a runtime-updated community Android model-code mapping. Community matches improve identity lookup but are not proof of board revision.
- Camera2 cannot expose every proprietary camera path on every vendor firmware. Reported autofocus state does not establish optical sharpness; an image capture alone is not an optical-quality test.
- Full battery capacity, internal resistance, PMIC rail and charging-IC pin measurements require appropriate test equipment and safe model-specific connections.
- ADB is not raw USB hardware access. MTP-only devices do not expose the ADB diagnostic channel.
- Generic iPhone diagnosis is not implemented.

See docs/DEVICE-IDENTIFICATION.md, docs/SCREEN-AND-CAMERA-TESTS.md, docs/MEASUREMENT-RIG-PROTOCOL.md, docs/BOARDVIEW-IMPORT.md, docs/REMOTE-TEST-CONTROL.md, THIRD-PARTY-NOTICES.md, docs/ARCHITECTURE.md, docs/DIAGNOSTIC-COLORS.md and SECURITY.md.
