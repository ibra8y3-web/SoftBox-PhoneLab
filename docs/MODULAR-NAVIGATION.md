# SoftBox Modular Navigation

## Product rule

The product name remains **SoftBox**. The master navigation follows the 36 sections in the official SoftBox development specification and keeps their numeric order. The sidebar is scrollable; selecting a section opens a dedicated section window with its audited features and implementation states.

## Implementation

- \`SoftBox.PhoneLab.Core/Models/SoftBoxModuleCatalog.cs\` is the single source of truth for all 36 modules and their feature metadata.
- Each module has a stable ID, specification order, group, Arabic/English names, description, section state and feature-level states.
- \`MainWindow.xaml\` contains the sidebar navigation entries; each item uses the corresponding stable module ID as its Tag.
- \`MainWindow.xaml.cs\` routes navigation to \`ModuleSectionWindow\` and maps only declared action IDs to existing commands/windows.
- \`ModuleSectionWindow\` is a shared page host. It only renders an actionable button when a real existing action is wired. Planned/unsupported entries are status information, not fake controls.
- \`tests/SoftBox.PhoneLab.Tests/SoftBoxModuleCatalogTests.cs\` checks the 36-module count/order, unique IDs, feature metadata, valid action identifiers and the absence of actions for Planned/Unsupported features.
- \`docs/SOFTBOX-MASTER-MODULE-STATUS.md\` is the audited feature matrix and phase roadmap.

## Add a section or tool without duplication

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
4. Sections 11–14: Firmware Center, USB Doctor, Smart Backup, Phone Link Studio.
5. Sections 15–19: Hardware Diagnostics, Network Diagnostics, Smart Repair AI, One-Click Diagnostic, Safe Recovery.
6. Sections 20–25: Workflow Automation, Batch Operations, Repair History, Workshop Manager, Parts Inventory, Service Reports.
7. Sections 26–28: Plugin Store, Plugin SDK, Offline Center.
8. Sections 29–36: Security & Privacy, User & Technician Management, Update Center, Notification Center, Help Center, Support & Logs, Settings, About.

## Safety and truthfulness

A model profile is not proof of board compatibility. A data read is not a functional test. Firmware SHA-256 is not publisher authentication. ADF/Fastboot generic discovery is not vendor Download/EDL/Preloader support. No section may be marked Tested or Verified solely because the WPF screen opens or CI builds successfully.
