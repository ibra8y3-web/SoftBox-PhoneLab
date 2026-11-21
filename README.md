# SoftBox PhoneLab

**Windows smartphone diagnostics workstation with an Android USB companion.**

PhoneLab discovers authorized Android phones over ADB, reads available device identifiers, runs permitted on-device tests, and records evidence for each component. It deliberately distinguishes actual test results from information merely read from Android.

## Current implemented capabilities

### Device detection and model profiles
- ADB auto-discovery every 3 seconds.
- Authenticated Android Wireless debugging pairing through the existing Google Platform-Tools (adb pair then adb connect), with separate pairing/connection IP addresses and ports, one-time code handling and cancellation. Requires Android 11+ and the same Wi-Fi network; see docs/WIRELESS-ADB-PAIRING.md.
- Reads manufacturer, marketing model, model number, product, device codename, hardware SKU, build fingerprint, board, SoC, bootloader, CPU and ABI.
- Matches a bundled curated catalog and automatically adds the Apache-2.0 community model-code mapping generated from Google's public Play supported-device list. The community catalog updates upstream daily and is cached locally by PhoneLab.
- Shows match confidence and evidence; an identity profile is not treated as a boardview file.

The curated catalog is a starter set. The additional community model-code database greatly broadens Android model-name matching, but it is not guaranteed to contain every regional variant or every phone ever sold. Community-only matches are shown as likely, not confirmed.

### Android App Manager
- Reads installed third-party packages and package details from the selected authorized phone.
- Installs or updates a selected local APK only after Manifest inspection and explicit confirmation.
- Installs a selected Base APK plus its split files through `adb install-multiple -r` after inspecting every Manifest and ZIP archive. SoftBox requires one Base, unique Split names, matching package names and Version Code, and re-checks each SHA-256 before sending the files.
- Android's package installer remains responsible for signer compatibility; SoftBox reports signature metadata but does not independently verify publisher certificates. Android rejects incompatible signer sets.
- Removes only a selected package that SoftBox re-confirms in Android's `pm list packages -3` output, after showing the package name and an explicit warning that local app data will be deleted.
- SoftBox reports install/removal success only when ADB returns `Success`; system packages and arbitrary package names are rejected. Private app-data management remains unsupported.
- See [Android App Manager safeguards](docs/ANDROID-APP-MANAGER.md).


### 3D component workspace
- Interactive exploded smartphone schematic with component selection and test-state colors.
- Imports model-specific board-map JSON files only when they identify an exact model/codename and include HTTPS source, reviewer and verification timestamp.
- The only shipped map is a generic educational fallback. No fake IC coordinates are generated for unknown devices.

### Screen and cameras
- Automatically opens the authorized phone screen inside the PhoneLab workspace using the official scrcpy Windows client. The bundled scrcpy archive is SHA-256 checked during CI packaging.
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
- On first authorized USB connection, Windows installs the bundled Agent APK and opens it. The phone displays a per-session consent dialog; the local-only ADB bridge starts only after the user taps Allow.
- Exports device identity, Agent telemetry, external meter readings, component results and test limitations in JSON.

### Professional workshop management and local records
- Offline repair-shop workspace for customers, repair orders, status tracking, intake symptoms, pre-repair findings, work performed, post-repair test results and estimates/final costs.
- Generates a unique repair-ticket number and a QR PNG that contains only that ticket number, not customer contact data.
- Customer directory, parts inventory, low-stock threshold flag, stock in/out movements and a capped activity history.
- Local user-account/audit, workshop and device activity databases are protected using Windows DPAPI CurrentUser scope with separate purpose identifiers. Legacy plaintext JSON is migrated to encrypted storage on first successful load; no customer records are uploaded to a cloud service.
- Workshop backups are exported as encrypted .sbxbackup files; legacy JSON backups remain importable. DPAPI backups are restorable only under the same Windows user context on the same Windows profile; they are not portable between different accounts/devices.
- Exports a technician-entered text report for each repair order.
- Creates printable Arabic/English intake receipts, preliminary estimates and service invoices from saved work-order data. Customer quote approval/rejection is recorded with its history, quoted amount, contact method and technician; these are local manual records, not electronic signatures.
- Records multiple partial/full payments against the currently approved quote, computes the remaining balance, prevents overpayment and prevents price edits/cancellation from invalidating existing money records. Each recorded payment has a printable receipt, but the application does not verify settlement with a bank or payment provider.
- These documents and ledger do not calculate taxes, process/refund payments or claim certified e-invoice compliance.
- Service analytics calculate delivered-order revenue, open estimates, reported symptom text frequency, device-model frequency, technician activity, delivery duration and inventory values from saved local records; analytics export is available as CSV.

### USB transport, Android evidence and firmware inspection
- Read-only ADB/Fastboot transport inspection. The report explains the difference between MTP, authorized ADB, Fastboot and vendor-specific Download/EDL/Preloader modes.
- Captures a bounded Android evidence bundle: recent logcat, system properties, battery and thermal service state, and boot uptime. A heuristic can flag log patterns worth reviewing for reboot/boot-loop investigation.
- Inspects a selected firmware archive locally, computes SHA-256 and looks for common Android OTA metadata/payload entries. It does not flash, unlock, wipe, execute package scripts or assert that a package is authentic or compatible.

### Workshop documents and service analytics
- From a selected repair order, create a printable intake receipt, preliminary estimate or service invoice using saved repair-order values.
- The estimate can be marked approved or rejected by recording the contact method, timestamp, quoted amount, technician and notes. Payment entries require approval for the current quote and keep a running balance. Approval and payment entries are manual workshop records, not an electronic signature or external payment confirmation. The invoice shows the recorded balance and payment history; tax calculation, payment processing/refunds and certified e-invoicing remain unsupported.
- The reports tab calculates operating metrics from saved repair orders and inventory and exports the analytics to UTF-8 CSV. Delivered-order revenue is not a profit or cash-collection figure.
- Dashboard Analytics reads the persistent local device history and notification inbox, showing known devices, activity over 24 hours/7/30 days, unread notifications, common event categories and a 14-day UTC trend. It reports recorded activity only; it does not claim a background job queue.

### Global workshop operations
- A dedicated Global Workshop workspace adds cross-field search and status filtering for repair orders, live operational counts, active-warranty tracking and technician timeline notes.
- Workshop profile settings include business name, country, address, international phone, email, optional tax registration reference, ISO 4217 three-letter currency code, culture/locale, time-zone ID, report language (Arabic/English) and default warranty duration.
- Delivered repair orders receive a warranty expiry calculated from their delivery timestamp and configured months. Warranty duration is policy data entered by the workshop; it does not determine statutory consumer rights.
- Generates print-friendly Arabic or English HTML reports that can be printed or saved as PDF through the browser. Exports repair orders and inventory as UTF-8 CSV for spreadsheet and ERP import.
- Workshop database schemas v1-v5 are validated and upgraded to schema v5 on load without discarding customer, order, inventory or event records; the same first successful load also migrates plaintext JSON to DPAPI-protected storage. The currency code labels values; no exchange-rate conversion or tax calculation is performed.

## Modular navigation

- A persistent right-side menu groups tools by workflow; each major capability opens its own dedicated workspace.
- New modules follow the convention in [docs/MODULAR-NAVIGATION.md](docs/MODULAR-NAVIGATION.md).
- International workshop administration adds per-business locale/currency settings, warranty tracking, technician notes, printable HTML and UTF-8 CSV exports.


## SoftBox 36-section product architecture

- The left navigation follows all 36 sections of the SoftBox master specification in order; each opens an independent section workspace with current tools and per-feature status.
- Existing services are shared and linked from the correct section instead of being cloned. Planned features are listed without fake executable buttons.
- The detailed audit, missing-feature matrix, module ownership and staged implementation order are in [docs/SOFTBOX-MASTER-MODULE-STATUS.md](docs/SOFTBOX-MASTER-MODULE-STATUS.md).
- Navigation and module addition rules are in [docs/MODULAR-NAVIGATION.md](docs/MODULAR-NAVIGATION.md).

## Repository modules

| Module | Purpose |
|---|---|
| src/SoftBox.PhoneLab.Desktop | Windows WPF app, live mirror, screen-photo analysis, measurement dashboard, workshop center, global workshop operations and read-only forensics |
| src/SoftBox.PhoneLab.Core | Device snapshots, model profiles, parsing and diagnostic evidence mapping |
| agent | Android PhoneLab Agent, Camera2 diagnostics and local ADB-forward bridge |
| data/device-profiles | Curated starter device identity catalog |
| data/boardmaps | Verified-map format and generic fallback |
| hardware/measurement-rig | Safe external measurement integration notes |
| tests | Parser, diagnostic, warranty and international formatting unit tests |

## Requirements

- Windows 10/11 x64.
- Official Android SDK Platform-Tools (adb.exe).
- Android phone with a data USB cable and USB debugging authorization.
- The Windows package includes Google Platform-Tools (ADB), SHA-256 checked scrcpy, SHA-256 checked TRACE Boardviewer, the Android Agent APK and the official Samsung USB driver installer in one download. The Samsung driver is optional and intended only for Samsung phones.
- The bundled ADB/Fastboot Platform-Tools, scrcpy, TRACE Boardviewer, QR generation library and Agent work offline after downloading this Windows artifact. Internet is needed only for optional model-catalog refreshes and online repair-guide search. USB debugging approval and any required OEM ADB driver remain local setup requirements.

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

1. Extract the single Windows package and start PhoneLab; ADB, scrcpy, Boardviewer and the Agent APK are included.
2. Enable USB debugging, unlock the phone and approve the Android system USB debugging trust prompt for this PC. MTP/file-transfer visibility alone is not an ADB diagnostic connection. Some phone brands require their Windows ADB driver.
3. PhoneLab detects the authorized device, opens the live screen inside its workspace, installs Agent and asks for a second confirmation on the phone. Tap Allow to enable the remote test bridge.
4. PhoneLab reads the identifiers and offers display, Camera2, touch, audio, flashlight, vibration, sensor and hardware-inventory tests.
5. Use the physical display photo analyzer with a real camera photo of the screen during a uniform test pattern.
6. Connect a supported, electrically safe serial measurement fixture for USB/battery voltage/current, temperature, optional capacity/internal resistance and named rails. Export the history to CSV.
7. Open مركز الصيانة to create work orders, record before/after evidence, manage stock, export a QR ticket and back up local records. Open Global Workshop for searchable operations, warranty tracking, technician history, international center settings and HTML/CSV exports.
8. Use سجلات Android to save private logs for review, or فحص حزمة Firmware to calculate SHA-256 and inspect OTA metadata without flashing.
9. Review every component's evidence and export a JSON report.

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
- ADB is not raw USB hardware access. MTP-only devices do not expose the ADB diagnostic channel. Generic Fastboot discovery cannot enumerate every Samsung Download, Qualcomm EDL, MediaTek Preloader or OEM recovery interface. OEM-specific drivers are not all included; the bundled optional driver covers Samsung only.
- The workshop store is local DPAPI-protected JSON persistence, not a multi-PC synchronized database. The DPAPI key material is managed by Windows for the current user; SoftBox does not export or synchronize it. The encrypted .sbxbackup is restorable under the same Windows user context, not a portable cross-account backup. CSV/HTML/JSON reports exported separately may contain personal data and are not automatically encrypted.
- Currency codes and cultures only format values: PhoneLab does not provide exchange rates, tax compliance calculations, e-invoicing certification or statutory warranty interpretation.
- The QR ticket contains only a repair identifier. It is not a cloud lookup link.
- Log analysis is heuristic and may find false positives or miss events outside the captured log window. Android logs can contain private data.
- Firmware tools verify local SHA-256, inspect OTA ZIP metadata and Samsung TAR/TAR.MD5 structure, and compare available device/region/board/rollback evidence through a fail-closed compatibility gate. Hashes and appended TAR.MD5 checks do not authenticate the publisher; missing board, region, signature or Anti-Rollback evidence remains unknown and blocks the gate. No automatic flashing, bootloader unlocking or data wipe is implemented.
- Generic iPhone diagnosis is not implemented.

See docs/PROFESSIONAL-WORKSHOP.md, docs/USB-TRANSPORTS-AND-FORENSICS.md, docs/DEVICE-IDENTIFICATION.md, docs/SCREEN-AND-CAMERA-TESTS.md, docs/MEASUREMENT-RIG-PROTOCOL.md, docs/BOARDVIEW-IMPORT.md, docs/REMOTE-TEST-CONTROL.md, THIRD-PARTY-NOTICES.md, docs/ARCHITECTURE.md, docs/DIAGNOSTIC-COLORS.md and SECURITY.md.


### Additional Network Diagnostics and Repair History tools

- Network Diagnostics can inspect the selected authorized Android phone's reported DNS servers and send one bounded ICMP Ping request from the phone itself to a technician-selected hostname or IP. The result displays Android's raw output; a missing ICMP reply is not treated as proof of Internet failure.
- Repair History supports text search across device, model, event, summary and evidence, filtering by event type, and exporting only matching entries to UTF-8 CSV. Exported serials remain masked and the device fingerprint is omitted.


## Additional real gap-closure tools — November 2026

- **Samsung Odin workflow:** SoftBox reads TAR entries without extracting them, verifies appended MD5 in TAR.MD5 files, records SHA-256, opens the technician-selected Odin executable and can inspect its Windows UI Automation tree. A guarded Start Invoke is enabled only when the exact process, Odin window, unique enabled Start control, visible selected package names, COM port and explicit technician confirmations are all present. If Odin does not expose the required controls, the action is blocked. This is not a documented Samsung service protocol and does not verify flash completion.
- **Firmware compatibility gate:** compares known manufacturer/model/product/board/region/rollback evidence and local integrity; any missing publisher signature, board or rollback evidence remains Unknown and blocks the gate.
- **Xiaomi Fastboot diagnostics:** on a verified Xiaomi/Redmi/POCO ADB phone, SoftBox confirms the current manufacturer, requests a confirmed reboot to Bootloader, waits for the same serial in Fastboot, then reads getvar variables, lock state, Verified Boot and raw Anti-Rollback values. The diagnostic reader remains read-only; separate guarded service actions handle only the explicitly supported flash steps.
- **Qualcomm / MediaTek transport readiness:** Windows PnP observations for common 9008 and Preloader/BROM USB IDs now produce driver/problem and missing-requirements reports. The tools do not open EDL/BROM or load programmers/Download Agents.
- **External measurement profiles:** technicians can import sourced JSON thresholds and compare real serial-fixture samples. No factory voltages or board limits are invented.
- **Repair knowledge:** local DPAPI-protected technician notes now support add/edit/delete/search and are prefiltered by the selected phone from Repair Advisor. Entries require source/evidence from the technician and are not represented as OEM facts.
- **Vendor properties:** read-only fixed Android property profiles cover Samsung, Xiaomi, Google Pixel, Motorola/Lenovo, OnePlus, OPPO, vivo, realme, Huawei/HONOR, TECNO and Infinix. These are data collection profiles, not proprietary service protocols.
- **Signed plugin workflows:** RSA-signed packages, user-reviewed publisher fingerprints, DPAPI-protected public-key trust, package-library management and permission-gated JSON workflows using the fixed read-only ADB preset allow-list are implemented. External DLL execution remains disabled.
- **Signed update pipeline:** the updater validates official release URLs, RSA publisher signature, per-file SHA-256, minimum supported version, stages the package, waits for SoftBox to exit, takes an install snapshot and creates an explicit rollback helper. The solution includes a release signer that builds the accepted manifest and RSA-PSS signature; tag publishing requires the `SOFTBOX_RELEASE_PRIVATE_KEY_PEM` GitHub Actions secret and refuses to publish unsigned packages.
- **Device coverage matrix:** Device Intelligence exports the loaded device-profile catalogue to CSV, including model identifiers, SoC aliases, Board IDs, map references and missing evidence. A model name or BoardMapId is not treated as a verified PCB map.
- **Xiaomi Fastboot ROM inspection:** Xiaomi Tools reads TAR/TGZ package metadata, product requirements, image names and flash/erase/unlock commands without extracting files or executing package scripts. It compares the package product with the last Product read from the selected phone's Fastboot state; it never flashes or unlocks.




## OEM recovery follow-up — 21 November 2026

See [OEM service and recovery workflows](docs/OEM-RECOVERY-WORKFLOWS.md) for implementation boundaries and external prerequisites.

- **Xiaomi Fastboot:** the restricted single-image path remains available. A separate multi-image workflow accepts only an extracted ROM folder and `flash_all.bat` or `flash_all_except_storage.bat`; it parses approved Fastboot lines without executing the OEM script, checks the exact Product and serial, requires an unlocked bootloader, hashes every image and repeats live checks before each write. It supports explicit userdata image/erase/format/wipe steps only from the clean-flash script with separate destructive consent. Unlock/relock, arbitrary commands, unsafe partitions and unsupported erase/format commands are rejected. Numeric ROM/device Anti-Rollback values are compared when both are available; otherwise manual acknowledgement is required.
- **Qualcomm QDL:** the public QDL CLI integration supports device list, chipinfo, explicit storage type, package inspection, dry-run and guarded flash. No Firehose programmer is bundled. Exact model/board/SoC, OEM package provenance, storage type, programmer authorization and physical verification remain required.
- **MediaTek:** scatter/image validation and DA/Auth hashing are implemented, with a recheck before launching the technician-supplied SP Flash Tool. BROM programming, DA signature verification, SLA/DAA authorization and automatic Download are not implemented without a model-specific signed OEM package.
- **Samsung:** Odin Start uses guarded Windows UI Automation only; no public supported Samsung Odin service API is claimed. Start is blocked if accessibility evidence is incomplete.
- **Android OEM directory:** supported official links are surfaced for Google/Motorola and vendor documentation; a link is not a per-model compatibility guarantee.
- The catalogue contains **36 master sections / 156 child features / 192 total entries**. Child statuses: **99 Implemented, 46 Tested, 6 In Development, 3 Planned, 2 Unsupported, 0 Verified**. Physical Xiaomi/Qualcomm/MediaTek validation, multi-phone cable/codec/hotplug validation and a signed production release remain open.


## OEM service implementation addendum — 21 November 2026

- Samsung: the selected authorized ADB device can be sent to Download Mode, and the Odin assistant can inspect the exact process/window and invoke Start once through UI Automation after explicit device/COM, firmware and backup confirmation. Start invocation is not a flash-success result; hardware and Odin-version testing remain required.
- Xiaomi: the new full-ROM action is linked from the Xiaomi Fastboot service window. The technician must extract the ROM first. The BAT file is parsed but never executed; supported Fastboot steps are issued separately with serial/Product/bootloader rechecks and image SHA-256 validation. Unsupported or ambiguous script commands fail closed, so not every Xiaomi ROM layout is guaranteed to be accepted.
- No physical Samsung or Xiaomi handset test is claimed by the code change or CI build.


## Device Access Diagnostics and Official Account Recovery — 21 November 2026

- A new read-only Android access diagnostic is available under Security & Privacy. It checks the selected authorized ADB serial, reads public `dumpsys device_policy` and `dumpsys window` output, and displays basic SIM/operator properties where the OEM build exposes them.
- The report distinguishes a visible lock screen from an unknown state; it does not reveal whether a PIN/Pattern is configured. Device Owner/Profile Owner indicators are management evidence, not an MDM removal action. Missing indicators do not prove that a device is unmanaged.
- Google FRP, Xiaomi Mi Account activation lock, Samsung FRP, screen-lock removal, MDM removal and SIM/network-lock bypass are explicitly catalogued as **Unsupported**, with no executable Run button.
- The window links to official Google, Samsung, Xiaomi and Android Enterprise recovery channels. Carrier network unlock remains a request to the original carrier based on its IMEI/contract eligibility checks; SoftBox does not write Baseband/NV.
- Read-only parser tests are included. Physical-device coverage across OEM models and Android releases remains unverified; no all-phone compatibility claim is made.

See [Device Access Diagnostics and Official Recovery](docs/DEVICE-ACCESS-AND-RECOVERY.md).
