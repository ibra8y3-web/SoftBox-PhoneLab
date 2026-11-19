# SoftBox PhoneLab

**Windows smartphone diagnostics workstation with an Android USB companion.**

PhoneLab discovers authorized Android phones over ADB, reads available device identifiers, runs permitted on-device tests, and records evidence for each component. It deliberately distinguishes actual test results from information merely read from Android.

## Current implemented capabilities

### Device detection and model profiles
- ADB auto-discovery every 3 seconds.
- Reads manufacturer, marketing model, model number, product, device codename, hardware SKU, build fingerprint, board, SoC, bootloader, CPU and ABI.
- Matches a bundled device-identity catalog and can update it from this repository.
- Shows match confidence and evidence; an identity profile is not treated as a boardview file.

The current bundled identity catalog is a starter set, not a complete list of every phone. Unknown model variants are displayed as unknown rather than guessed.

### 3D component workspace
- Interactive exploded smartphone schematic with component selection and test-state colors.
- Imports model-specific board-map JSON files only when they identify an exact model/codename and include HTTPS source, reviewer and verification timestamp.
- The only shipped map is a generic educational fallback. No fake IC coordinates are generated for unknown devices.

### Screen and cameras
- Launches the official scrcpy Windows client for live USB screen mirroring. The archive's SHA-256 is checked against the official scrcpy Windows installation page before extraction.
- Agent displays uniform color test patterns. A camera photograph of the physical screen can be analyzed for candidate pixel outliers and luminance non-uniformity. This is a screening aid, not a calibrated optical inspection.
- Android Camera2 enumerates logical camera IDs and exposed physical camera routes, reads available lens metadata, and attempts a real JPEG capture for each route. Vendor-hidden cameras and inaccessible physical routes remain unavailable/review.

### Battery and electrical measurements
- Reads Android fuel-gauge information when exposed: charge counter, current now/average, remaining energy, voltage, temperature and API 34+ cycle count.
- Adds a serial measurement dashboard for compatible USB/bench instruments using the read-only JSON-Lines v1 protocol documented in docs/MEASUREMENT-RIG-PROTOCOL.md.
- Supports measurement values for USB input, battery voltage/current, board temperature, power and named rails when the connected instrument provides them.
- It does not issue voltage/current setpoints or power commands.

Fuel-gauge data is not a certified battery-capacity test. A real capacity estimate needs a controlled, sufficiently long charge/discharge measurement or an external battery analyzer. A USB meter cannot measure hidden PMIC rails without a properly engineered model-specific probe.

### Repair knowledge and reports
- On-demand iFixit repair guide search opens original source pages; PhoneLab does not mirror their copyrighted repair content.
- Exports device identity, Agent telemetry, component results and the interpretation/limitations of each test in JSON.

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
- Internet access is required for the first scrcpy installation, optional device-catalog updates and repair-guide search.

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
4. Use live screen mirror, display pattern, Camera2 route capture, touch, speaker, microphone, vibration and sensor checks.
5. Use the physical display photo analyzer with a real camera photo of the screen during a uniform test pattern.
6. Connect a supported, electrically safe serial measurement fixture for external voltage/current/temperature readings.
7. Review every component's evidence and export a JSON report.

## Diagnostic color contract

- Green: a named functional test passed.
- Yellow: a value or result needs review.
- Red: a named test reported failure.
- Blue: data was read; health is not proven.
- Gray: not tested or unavailable.

## Important engineering limits

- This project does not yet contain model-specific internal PCB/boardview files for every phone. Exact board coordinates, IC positions and socket placements cannot be fabricated from an Android model string.
- The current identity database is only a starter set. We need to grow it with verified manufacturer model identifiers and licensed repair documentation.
- Camera2 cannot expose every proprietary camera path on every vendor firmware, and an image capture alone is not an optical-quality test.
- Full battery capacity, internal resistance, PMIC rail and charging-IC pin measurements require appropriate test equipment and safe model-specific connections.
- ADB is not raw USB hardware access. MTP-only devices do not expose the ADB diagnostic channel.
- Generic iPhone diagnosis is not implemented.

See docs/DEVICE-IDENTIFICATION.md, docs/SCREEN-AND-CAMERA-TESTS.md, docs/MEASUREMENT-RIG-PROTOCOL.md, docs/ARCHITECTURE.md, docs/DIAGNOSTIC-COLORS.md and SECURITY.md.
