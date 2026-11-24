# Phone Link Studio, universal discovery and OEM resources

## Screen transport model

PhoneLab uses separate transport adapters rather than claiming that every phone speaks one universal screen protocol.

| Device / mode | Detection evidence | Screen route | Remote control | Current boundary |
|---|---|---|---|---|
| Supported Android OEM phone | Windows PnP plus independently authorized ADB serial | scrcpy docked in PhoneLab | Yes, within Android/OEM permissions and user authorization | Requires booted Android, ADB interface, USB debugging authorization and supported scrcpy behavior |
| iPhone / iPad | Apple Lockdown device list from user-selected libimobiledevice tools; Windows Apple VID is only a USB hint | Dedicated AirPlay-Windows window with its own native screen host | No; viewing only | Same local network, owner must select receiver from Control Center; no passcode/touch injection; no shared Android host |
| Miracast-capable Android / OEM phone | Owner opens Cast/Screen Mirroring on phone; Windows receiver discovery is handled by Wireless Display | Native Windows Wireless Display app / Connect app | No control from SoftBox | Requires Miracast on both devices and Windows optional feature; this native session is outside the SoftBox dock |
| Android MTP-only device | Windows USB PnP/MTP device | None from MTP alone | None | File-transfer visibility is not screen capture or ADB authorization |
| Fastboot / Qualcomm EDL / MediaTek Preloader | Windows PnP/VID/PID or relevant service tools | No ordinary live screen | No ordinary Android UI control | These are boot/service modes, not screen protocols |
| Unknown USB device | Windows PnP identity only | No route is guessed | No route is guessed | Exact device identity and supported transport remain unknown |

### Android: ADB and scrcpy

- The fixed right-side panel remains visible beside the device/component column.
- The selected ADB serial is rechecked with `get-state` immediately before scrcpy launch.
- A best-effort wake-up key event may wake the panel; it does not unlock the phone or bypass a screen lock.
- The selected phone must be authorized by Android. An unauthorized/offline/missing serial is not reported as connected for screen use.
- scrcpy stdout/stderr is captured in a bounded in-memory buffer and included in startup failure details. Failure analysis distinguishes ADB unauthorized/offline/missing device/server conflict, missing runtime, integrity mismatch, unsupported platform, process exit and window timeout.
- The retry timer is throttled per device; retries do not repeat Agent installation or general device inspection.

### iPhone/iPad: AirPlay viewing

- SoftBox manages a separately installed `airplay-windows.exe` from the upstream <https://github.com/moieric11/AirPlay-Windows> project.
- SoftBox does not bundle or redistribute the GPL-3 receiver. The operator must independently download and review the upstream release/licence, then select the executable in SoftBox.
- SoftBox records the selected path in local preferences and displays a SHA-256 file fingerprint. A file hash is not publisher authentication; use the upstream release and normal Windows security checks.
- The receiver creates its own native display window and SoftBox docks it into a dedicated `ScrcpyWindowHost` inside the independent AirPlay receiver window. The Android `PhoneScreenHost` is never passed to the AirPlay service.
- SoftBox can also open the Windows Miracast setup pages as a separate fallback. Microsoft requires the Wireless Display optional feature and the native Wireless Display/Connect app; both the PC and the phone must support Miracast. This OS-level session is not embedded in SoftBox.
- The iPhone and Windows PC must be on the same LAN. On iPhone/iPad, the owner opens Control Center, chooses Screen Mirroring and selects the receiver name.
- This adapter provides screen viewing only. SoftBox does not inject touch, enter a passcode, unlock iOS, or defeat Activation Lock.
- DRM-protected streaming apps can refuse screen mirroring. AirPlay is not a substitute for Apple Devices USB support or an iOS diagnostic agent.
- SoftBox preserves manual AirPlay priority: before start it stops the Android mirror and detaches the Android host; it refuses to switch while an Android screen recording is active. The AirPlay image then appears only in the separate iPhone receiver window. Automatic scrcpy retry is suppressed while the AirPlay process is running.

## Universal Device Discovery

The discovery workspace shows two evidence sources side by side:
1. Windows USB PnP: instance ID, friendly name, manufacturer, device class, service, VID/PID, Windows problem code and PnP status.
2. Android ADB: serial-masked display name and the authorization state returned by the ADB device list.

A Windows USB record is not automatically joined to an ADB serial by guessing. Manufacturer, friendly name and VID/PID indicate a likely USB vendor; they do not prove the precise model, board revision, boot state or software capability. A recognized Huawei/OPPO/realme/vivo/Xiaomi/Samsung vendor can use the Android ADB route only when its actual firmware exposes ADB and the owner authorizes this PC. Proprietary vendor service protocols remain model- and version-specific.

## Components and saved diagnostic evidence

- The component list loads before a device is attached and starts with `Not tested`.
- Selecting a component shows its name, current diagnostic status, explanatory description and available evidence in the persistent component rail even while an embedded tool is open.
- The existing Android Agent test window is the entry point for tests that require an Agent, a booted Android OS and the owner's on-device consent.
- `DiagnosticEngine` continues to distinguish `Pass`, `Failed`, `Review`, `DataRead`, `Unavailable` and `NotTested`. Reading a battery percentage, RAM size, feature declaration or camera route is not a functional test.
- Board-level IC, solder, rail and connector tests are not synthesized by software. They require exact schematics, safe fixtures/probes or manual inspection.
- Actual snapshots are recorded in `DiagnosticEvidenceStore` under the current Windows user with DPAPI protection. Each entry stores a SHA-256 one-way device fingerprint, masked serial, manufacturer/model, component ID/name, status, evidence source and UTC timestamp. Identical repeated snapshots are deduplicated.
- The evidence history screen can be filtered to the currently selected authorized ADB device. Raw serials and credentials are not written to this history file.

## OEM driver and support resources

The current catalog contains **33 official vendor resource entries**. It deliberately mixes resource types rather than claiming that every vendor provides a direct standalone driver:

- Dedicated Windows USB driver sources (for example Google and Samsung).
- Regional MTP driver downloads (for example the listed vivo resource).
- OEM desktop companion/software portals (for example Apple Devices, HONOR Suite, MediaTek Online and other vendor utilities).
- Manufacturer support/download portals where the exact package is selected by model and region.

For each entry SoftBox shows the resource kind and support notes. The current status is intentionally **not verified for the exact selected model** until a technician chooses a specific package. A model family name, a VID, or a support-page URL is not proof that a driver exists for that exact handset.

Before installing a downloaded driver:
1. Verify the exact model, hardware revision if provided, region, Windows edition/version and architecture.
2. Check the vendor's own package instructions and publisher signature.
3. Use the existing Windows driver signature verifier on the selected local driver package/CAT. A valid signature proves a trust/signature property; it does not prove that the package matches the phone's hardware ID.
4. Do not disable Windows driver-signature enforcement and do not install a generic or unsigned driver just to make a device appear.

SoftBox does not silently download or install a driver, does not alter the Windows Driver Store from the resource list, and does not treat an MTP connection as an authorized ADB session.

## Forgotten screen-lock assistance

The owner recovery assistant provides documented official guidance based on platform, manufacturer, OS/One UI version, previous credential availability, the elapsed 72-hour window, linked-account access, backup availability and proof of ownership.

- Apple previous-passcode recovery is only suggested when iOS/iPadOS 17+ requirements are met and the owner knows the previous passcode within the documented 72-hour window.
- Samsung Previous Screen Lock is only suggested for eligible Galaxy/One UI 6.1+ cases when the owner knows the previous lock method within the documented 72-hour window.
- Otherwise the assistant prefers account recovery, official manufacturer support or clearly warned official erase/restore guidance when the applicable requirements are present.
- It never asks the user to type a PIN, pattern, passcode, Apple Account password, Google password or verification code into SoftBox.
- It does not bypass PIN/Pattern, FRP, Google Device Protection, Samsung/Mi/Apple activation locks, MDM, carrier locks or enterprise ownership controls.
- Factory reset may erase local data and can still require the previously linked account during setup. The assistant does not issue a reset command.

## OEM source availability and INF matching

The OEM resource center now has a live source-page check:
- Uses HTTPS HEAD and falls back to GET only when HEAD is unsupported.
- Follows at most five HTTPS redirects and blocks a redirect downgrade to HTTP.
- Reports HTTP status, request method, redirect count and the final URL where applicable.
- Does not read/download the driver payload, install anything or claim that a package for the exact model exists. A reachable support page is only a reachable source page.

The per-device INF matcher starts from the exact Windows PnP Hardware IDs read through SetupAPI:
- Parses `[Manufacturer]` and the referenced `Models` rows.
- Checks exact ID matches and Windows architecture / minimum major-minor-build decorations.
- Keeps unresolved ProductType/SuiteMask decorations as Unknown rather than guessing.
- A match is a **candidate INF match**. When multiple decorated model sections exist, the checker does not claim it has reproduced SetupAPI's final selection or driver-store ranking.
- The next required checks are the source package's CAT signature and membership for INF/SYS members using the existing Microsoft SignTool path. A valid signature still does not prove the driver is correct for the device.

## Verification status

Automated tests cover resource counts/aliases, USB transport classification, lock-recovery decision rules, screen failure classification, settings persistence and encrypted diagnostic-history behavior. Hosted CI does not certify a physical handset.

The following still require physical verification on target hardware:
- Samsung SM-A315G with USB/ADB, embedded scrcpy, screen/touch/audio tests and re-plug/driver recovery.
- A supported iPhone/iPad and Windows PC on the same LAN with the independently installed AirPlay receiver.
- Huawei, OPPO, realme, vivo, Xiaomi, HONOR, TECNO, Infinix and other representative Android models to confirm their individual USB identity, ADB availability and model-specific driver state.
- Specific downloaded OEM driver packages against their real Windows hardware IDs and signer/catalog metadata.


## Windows Miracast fallback

The Miracast setup window opens the native Windows Projecting to this PC settings and Optional Features pages, then provides the official Microsoft steps. It does not silently install Windows optional features, change discoverability/privacy settings, start a receiver session, or claim to embed the native receiver inside SoftBox. A supported PC and Miracast-capable phone are both required. See <https://support.microsoft.com/en-us/windows/hardware/display-graphics/screen-mirroring-and-projecting-to-your-pc-or-wireless-display>.


## Dedicated iPhone & iPad diagnostics (Section 37)

### Apple device identity and connection

- The iPhone Center requires a user-selected folder containing `idevice_id.exe` and `ideviceinfo.exe` from a trusted Windows build of libimobiledevice. `idevicediagnostics.exe` is optional but enables automatic GasGauge cycle/capacity reads. The tools are not downloaded automatically or bundled without a reviewed source, version and licence.
- Discovery runs `idevice_id -l` and reads each returned device with `ideviceinfo -u <device-id> -x`. The process uses argument arrays (not a shell), bounded timeouts/output, a bounded device count and validates device identifiers before use.
- A successful XML plist read records ProductType, iOS version/build, model identifiers, available storage fields, current charge when exposed and a masked serial. When `idevicediagnostics.exe` is present, SoftBox runs the read-only `idevicediagnostics -u <UDID> diagnostics GasGauge` command and parses CycleCount, DesignCapacity, FullChargeCapacity and Status. Raw UDID/serial is used only in memory to make the one-way evidence fingerprint; the evidence store retains a masked serial and fingerprint, not the raw value.
- `BatteryCurrentCapacity` is presented only as current charge if it is an integer from 0–100. It is never converted to battery maximum capacity or battery health. GasGauge cycle count and capacity values are separately identified; an estimated maximum-capacity percentage is shown only when the response status is Success and the values fit the documented scale heuristic. The raw capacities and estimation basis remain visible.
- A Windows Apple VID (05AC) entry without a Lockdown response is displayed as USB presence only. The technician should unlock the phone and accept Apple's Trust This Computer prompt; USB presence is not full model identification or an authenticity verdict.

### Parts, battery and physical component checks

- Parts & Service History: the technician records the status visible in Settings > General > About. The available part categories and history depend on the iPhone model/iOS release. No entry in the history is not proof that a part was never replaced.
- Battery Health: the technician records Maximum Capacity, cycle count, manufacture/first-use dates and service messages only when the iPhone itself displays them. A current charge percentage is not maximum capacity.
- Physical checks: the technician records separate outcomes for image/brightness/pixels, touch grid/edges, rear/front cameras, Face ID, speaker/earpiece, microphones, proximity/ambient/motion sensors, charging/data port and wireless charging. A pass/fail is not pre-filled and requires the technician's observation note.
- The three workflows save component-specific evidence to the existing DPAPI-protected diagnostic evidence store. Each record includes its source and timestamp.

### Device originality assessment

- The assessment combines three independent clues: successful Apple Lockdown response, the technician's manually reviewed Apple Check Coverage page result, and the on-device Parts & Service History summary.
- A recognized serial or successful Lockdown exchange is not proof that the complete handset is genuine; copied serials, replaced boards and modified devices can create inconsistent signals.
- Results are therefore evidence states (compatible clues / review required / insufficient evidence), never a guaranteed “100% original” or “copy” classification. Physical inspection and official service diagnostics remain necessary for a high-confidence decision.

### iPhone verification boundaries

- Automated tests cover Lockdown/GasGauge plist parsing, Apple's standard plist DOCTYPE without external resolution, malformed/oversized input rejection, identity masking, GasGauge cycle count/capacity interpretation and the distinction between current charge and battery health.
- A physical iPhone/iPad plus a real, trusted Windows libimobiledevice package is still required to validate actual Lockdown pairing, different iOS releases, multiple connected devices, AirPlay streaming and shutdown.
