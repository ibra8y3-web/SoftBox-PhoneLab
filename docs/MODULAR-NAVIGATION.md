# SoftBox Modular Navigation

## Product rule

The product name remains **SoftBox**. The master navigation follows the 37 sections in the official SoftBox development specification and keeps their numeric order. The sidebar is scrollable; selecting a section opens a dedicated section window with its audited features and implementation states.

## Implementation

- \`SoftBox.PhoneLab.Core/Models/SoftBoxModuleCatalog.cs\` is the single source of truth for all 37 modules and their feature metadata.
- Each module has a stable ID, specification order, group, Arabic/English names, description, section state and feature-level states.
- \`MainWindow.xaml\` contains the sidebar navigation entries; each item uses the corresponding stable module ID as its Tag.
- \`MainWindow.xaml.cs\` routes navigation to \`ModuleSectionWindow\` and maps only declared action IDs to existing commands/windows.
- \`ModuleSectionWindow\` is a shared page host. It only renders an actionable button when a real existing action is wired. Planned/unsupported entries are status information, not fake controls.
- \`tests/SoftBox.PhoneLab.Tests/SoftBoxModuleCatalogTests.cs\` checks the 37-module count/order, unique IDs, feature metadata, valid and unique canonical action identifiers and the absence of actions for Planned/Unsupported features.
- \`docs/SOFTBOX-MASTER-MODULE-STATUS.md\` is the audited feature matrix and phase roadmap.

## Add a section or tool without duplication

The current catalog enforces a single canonical executable action for each existing tool. Where a feature is relevant elsewhere, the module page explains where the shared tool lives rather than showing another Run button. Workshop and global-workshop shortcuts can open a specific existing tab; they do not create another implementation.

1. Search the repository and the feature matrix to confirm the function does not already exist.
2. If it exists, keep the original service and move/link its navigation entry to the correct canonical module. Do not create a second implementation.
3. If the same tool is relevant in more than one module, share the action/service and document the relationship; do not copy its implementation or storage.
4. For a new section, add a stable ID and order to \`SoftBoxModuleCatalog\`, then add one sidebar entry with the same ID.
5. For a new real tool, implement it in the appropriate service/module, add error handling, logging and tests, then add a feature entry. Assign an ActionId only after the action is connected to real behavior.
6. Update the feature matrix and run the complete Windows build/test workflow.
7. Use Planned until development starts, In Development while incomplete, Implemented when real code exists, Tested when tests cover it, and Verified only after the intended real environment has been exercised with evidence.

## Current navigation groups

1. Sections 01–03: Dashboard, Device Center, Device Intelligence.
2. Sections 04–05: Android Service Center, APK Lab.
3. Sections 06–10: Samsung, Xiaomi, Android Brands, Qualcomm, MediaTek.
4. Sections 11–14: Firmware Center, USB Doctor (including 33 OEM resources), Smart Backup, Phone Link Studio (Android ADB/scrcpy, optional iPhone AirPlay viewing and Windows-native Miracast setup handoff).
5. Sections 15–19: Hardware Diagnostics (with encrypted evidence history), Network Diagnostics, Smart Repair AI, One-Click Diagnostic, Safe Recovery (including owner-safe forgotten-lock guidance).
6. Sections 20–25: Workflow Automation, Batch Operations, Repair History, Workshop Manager, Parts Inventory, Service Reports.
7. Sections 26–28: Plugin Store, Plugin SDK, Offline Center.
8. Sections 29–36: Security & Privacy, User & Technician Management, Update Center, Notification Center, Help Center, Support & Logs, Settings, About.
9. Section 37: dedicated iPhone & iPad Center (Apple Lockdown identity, Parts & Service History capture, battery-health capture, physical technician checks and evidence review).

## Safety and truthfulness

A model profile is not proof of board compatibility. A data read is not a functional test. Firmware SHA-256 is not publisher authentication. ADF/Fastboot generic discovery is not vendor Download/EDL/Preloader support. No section may be marked Tested or Verified solely because the WPF screen opens or CI builds successfully.


## Cross-platform transport ownership

- Section 02 owns the universal PnP/ADB discovery workspace. Windows USB PnP and Android ADB inventories remain separate evidence sources unless an explicit supported identity mapping exists.
- Section 12 owns OEM resource discovery and the existing local driver-signature verifier. Resource links are not treated as installed drivers or universal model compatibility.
- Section 14 owns the Android screen dock. Android uses the authorized ADB/scrcpy path. iPhone/iPad AirPlay is launched from Section 37 in a separate top-level receiver window with its own `ScrcpyWindowHost`; it never attaches to the Android screen host and remains view-only.
- Section 15 owns the DPAPI-protected component evidence history. The existing Agent test center remains the canonical entrypoint for real Android Agent tests.
- Section 19 owns owner-safe forgotten-lock recovery guidance. It does not provide lock, FRP, Activation Lock, MDM or account bypass actions.


## Dedicated Apple workspace (Section 37)

- iPhone discovery uses the user-selected `idevice_id.exe` and `ideviceinfo.exe` pair from a trusted libimobiledevice distribution. SoftBox does not silently download or bundle these utilities.
- The Lockdown parser accepts bounded XML plist data, rejects DTD/external entities, allow-lists identity/storage/current-charge fields and masks serial/UDID in UI and saved evidence.
- Parts & Service History, Battery Health and physical checks are separate technician workflows. The technician must read the official on-device screen or perform the physical check; SoftBox does not pretend to trigger these restricted iOS tests.
- Apple coverage is opened in the browser; the operator enters the serial directly on Apple. SoftBox does not transmit the serial or scrape a coverage verdict.
- An authenticity evidence summary reports only compatible clues, review flags or insufficient evidence. It does not claim an absolute original/copy verdict.
- AirPlay has its own window and native host. Android scrcpy remains in the Android panel; opening or stopping AirPlay cannot embed its window in that panel.
