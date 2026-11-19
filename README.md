# SoftBox PhoneLab

**Professional smartphone diagnostics workstation for Windows + Android.**

SoftBox PhoneLab is a standalone project. It contains a Windows WPF diagnostic console, a reusable C# diagnostic core, and an Android companion Agent. It is designed for authorized diagnosis of real phones connected through USB.

## Repository modules

| Module | Purpose |
|---|---|
| src/SoftBox.PhoneLab.Desktop | Windows desktop app, device discovery, 3D workspace, reports |
| src/SoftBox.PhoneLab.Core | Device models, parsers, diagnostic state machine and evidence interpretation |
| agent | Android PhoneLab Agent and local ADB-forward bridge |
| data/boardmaps | Model-map catalogue; generic diagram is explicitly not model-accurate |
| tests | Unit tests for parsers and diagnostic color/evidence rules |
| .github/workflows | Windows build/test/publish and Android APK build |

## Desktop capabilities

- Auto-polls ADB every 3 seconds and lists connected, unauthorized and offline Android devices.
- Reads available model/manufacturer/build identifiers, board/SoC hints, CPU description and cores, CPU ABI, RAM, /data usage, display resolution/density, baseband and Android battery-service observations.
- Shows an interactive WPF 3D exploded smartphone schematic; rotate with drag, zoom with mouse wheel, and select components in the model or component list.
- Associates each component with a diagnostic evidence state and explanatory note.
- Reads Agent test results through an ADB-forwarded loopback connection.
- Exports a JSON report with device observations, test evidence and hardware-limit disclaimers.

## Color contract

- Green: named functional test passed.
- Yellow: warning/review needed.
- Red: named test reported failure.
- Blue: system information was read; hardware health is not proven.
- Gray: not tested or unavailable.

A successful ADB connection never turns every part green. The PMIC, charging IC, board traces and solder joints remain gray unless a suitable evidence-backed test exists.

## Requirements

- Windows 10/11 x64.
- .NET 10 SDK to build; the published Windows artifact is self-contained.
- Official Android SDK Platform-Tools (adb.exe).
- Android phone with USB data cable. Enable Developer options > USB debugging and approve the PC RSA prompt.
- Android Studio or JDK 17 + Gradle 8.11.1 to build the Agent APK.

## Install Android Platform-Tools

Run PowerShell as your normal user:

    Set-ExecutionPolicy -Scope Process Bypass
    .\scripts\install-platform-tools.ps1

The script downloads the official Google Platform-Tools archive and sets the current user's SOFTBOX_ADB environment variable. Restart PhoneLab afterward.

## Build Windows app

    dotnet restore .\SoftBox.PhoneLab.sln
    dotnet test .\SoftBox.PhoneLab.sln -c Release
    dotnet publish .\src\SoftBox.PhoneLab.Desktop\SoftBox.PhoneLab.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o .\artifacts\PhoneLab-win-x64

Launch SoftBox-PhoneLab.exe.

## Build and install Android Agent

    cd .\agent
    gradle :app:assembleDebug

Install the APK:

    adb install -r .\app\build\outputs\apk\debug\app-debug.apk

Open SoftBox PhoneLab Agent on the phone. Keep it in the foreground while synchronizing tests from Windows.

## Typical workflow

1. Open PhoneLab on Windows.
2. Connect an Android phone with a data-capable USB cable.
3. Approve the ADB debugging trust prompt on the phone.
4. Select the phone; PhoneLab reads the Android system observations.
5. Install/open the Agent if on-device tests are needed.
6. Run display, touch, speaker, microphone, vibration, camera and sensor checks from the phone.
7. Return to Windows and press Update Agent.
8. Inspect component colors and evidence, then export a JSON report.

## Important limitations

- ADB is not the same as raw USB hardware access. MTP-only connections do not provide this diagnostic interface.
- Some Android fields are hidden, vendor-specific or inaccurate on some devices.
- Battery level/voltage/temperature from Android are observations, not a calibrated battery-capacity test.
- Display and touch tests currently require user interaction; the app does not claim automated dead-pixel detection.
- Camera testing uses the installed camera app and may return only a thumbnail.
- The current 3D model is a generic educational fallback. It is not an exact internal drawing or PCB layout for the detected device.
- Exact IC positions and boardview require legitimate, model/variant-specific documentation and a reviewed map.
- Electrical diagnosis of charging rails, PMIC, short circuits, connectors, solder joints and water damage requires external measurement equipment.
- Generic iPhone diagnosis is not implemented. Future Apple support must use authorized diagnostics paths.

See docs/ARCHITECTURE.md, docs/DIAGNOSTIC-COLORS.md, docs/DEVICE-COVERAGE.md, and SECURITY.md.
