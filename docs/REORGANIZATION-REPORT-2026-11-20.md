# SoftBox Reorganization Report — 20 November 2026

## Result

No tool implementation or service class was physically relocated in this pass: the audit found that the existing 36-section catalog already had the correct owners for most tools. Instead, the navigation ownership was normalized so shared windows have one canonical launch action, while distinct capabilities inside a shared workspace open their own tab. This avoids both repeated buttons and duplicate service implementations. The shared `ModuleSectionWindow` continues to show an executable action only when a feature is wired to a real command.

## Existing canonical ownership retained

| Existing tool/service | Existing canonical section | Action taken |
|---|---|---|
| ADB device discovery and device list | Device Center | Preserved; shared discovery remains the central device source |
| Android model/property matching | Device Intelligence | Preserved; no duplicate brand-specific identity engine created |
| Agent bridge and device-side diagnostic tests | Android Service Center / Hardware Diagnostics | Preserved; device consent and actual hardware requirements retained |
| scrcpy screen/control integration | Phone Link Studio | Preserved; no second mirroring service added |
| ADB/Fastboot transport inspection and Samsung USB driver | USB Doctor | Preserved; no false Samsung Download Mode/EDL support claim added |
| Firmware archive inspection | Firmware Center | Preserved; no claim of signature or compatibility verification added |
| Workshop customer/order/inventory store | Workshop Manager / Parts Inventory | Preserved; existing local persistence was not replaced |
| TRACE Boardviewer launch and board-map import | Device Intelligence | Preserved; third-party Boardviewer remains separate |

## Additions to existing sections

| Section | New item | Navigation link | Notes |
|---|---|---|---|
| 05 — APK Lab | Local APK inspector and two-file version comparison | `OpenApkLab` | Reads APK metadata, compares version fields/permissions/archive paths; signature discovery only, not cryptographic verification |
| 16 — Network Diagnostics | Local Windows network diagnostics | `OpenNetworkDiagnostics` | Reads local adapters and makes only a user-requested DNS/one-host ICMP probe; Android-private Wi-Fi is not inferred |

## Files changed for navigation and ownership

- `src/SoftBox.PhoneLab.Core/Models/SoftBoxModuleCatalog.cs`: retained all 36 section IDs and all feature records, normalized duplicated launch aliases, registered 52 unique executable action IDs and added Qualcomm/MediaTek filtered USB entrypoints.
- `src/SoftBox.PhoneLab.Desktop/MainWindow.xaml.cs`: routed Android capabilities to their specific shared-workspace tabs and added Qualcomm/MediaTek routes.
- `src/SoftBox.PhoneLab.Desktop/AndroidDeviceToolsWindow.cs`: added an explicit initial-tab argument and persisted the selection as window state.
- `src/SoftBox.PhoneLab.Desktop/UsbDoctorWindow.cs`: added actual Windows PnP filtering for Qualcomm and MediaTek VID/PID identity categories.
- `tests/SoftBox.PhoneLab.Tests/SoftBoxModuleCatalogTests.cs`: strengthened the canonical-action test to reject duplicate action IDs.
- Duplicate navigation aliases were removed from non-canonical feature rows and the old generic Android launch alias was replaced by three purpose-specific IDs. **No tool implementation, service, data store, section, or source file was deleted.**

## Preservation checks

- Product and application identity remain SoftBox.
- Existing 36 section IDs and sidebar section positions remain unchanged.
- Existing WPF/.NET 10 and Kotlin Android Agent structure remains unchanged.
- No existing source file or user data store was deleted or replaced.
- No new external runtime package was added.
- CI run #65 succeeded with 53 tests (0 failures), Windows x64 publish, Android Agent build and tool bundling, including the APK two-file comparison: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44754536968

Future moves should occur only when a specific existing tool has a proven ownership mismatch, after searching all navigation references and preserving its service, tests, shortcuts and dependencies.
## Parts inventory organization

- The existing Parts Inventory owner remains section 24. The advanced catalog editor adds supplier/category/pricing fields and exposes the existing stock ledger without creating a second inventory database.
- Repair-order part consumption is launched from the selected repair order, uses the same WorkshopStore instance, and records both a usage snapshot on the order and a stock movement.
- The same inventory service is used for manual adjustments and repair-order consumption; no duplicated part-balance engine was introduced.
## Manufacturer sections

- Samsung Tools and Xiaomi Tools open a read-only vendor diagnostics window only when the selected authorized device's detected manufacturer matches the requested vendor. Redmi and POCO are mapped to the Xiaomi property profile.
- Android Brands uses the same fixed-key read-only property collector for other manufacturers, avoiding duplicated ADB engines.
- Qualcomm and MediaTek sections open the shared USB Doctor with their own filter selected. The view enumerates actual Windows PnP devices and filters the registered Qualcomm 05C6:9008 or MediaTek 0E8D:0003 identity hints; it does not open an EDL/BROM channel, load a programmer/DA or claim firmware support.
## Smart Backup

- The existing `BackupArchiveService` remains the owner of ZIP Manifest and per-file SHA-256 generation/verification.
- New `EncryptedBackupService` wraps the existing ZIP archive in an authenticated `.sbx` container without replacing or changing the ZIP format.
- Android Backup UI exposes an explicit encryption checkbox; normal ZIP output remains supported and clearly labeled unencrypted.
## Plugin inspection ownership

- Plugin Manifest validation and ZIP package safety checks are implemented in the shared Core layer; the Plugin Store UI calls the same inspector.
- No plugin DLL is loaded by the inspector. Signature trust, install, update, uninstall and runtime execution remain separate planned capabilities.
## Backup restore path

- Restore is an additive operation on the existing AndroidBackupService and AndroidDeviceToolsService. It reuses the central ADB client, the existing archive verifier and SBX authentication service.
- Restore always generates a separate destination folder and pushes only verified files; it does not call delete, overwrite, format or private-app-data APIs.
## Phone Link Studio ownership

- Screen mirror and screen recording use the same ScreenMirrorService and the currently selected ADB-authorized device; there is one active scrcpy session in this implementation.
- The recording UI is a separate action under Phone Link Studio. It does not create a second ADB engine, does not record the Windows desktop and documents that stopping a recording also closes the active mirror session.

## Canonical navigation and section-specific workspaces — final pass

- The 36 existing sections and their display order are unchanged. No extra empty top-level sections were created because the supplied 36-section specification was already represented by the catalog.
- The three Android Service Center entries now have distinct actions: `OpenAndroidFileManager` selects the file tab, `OpenAndroidAppManager` selects the application tab and `OpenAndroidCommandCenter` selects the allow-listed read-only command tab. `OpenAndroidBatchTools` selects the batch tab. They still share one serial-authorized Android service/window, not separate duplicated ADB engines.
- Qualcomm and MediaTek now have section-specific entrypoints into the shared USB Doctor. The selected filter is applied to enumerated Windows PnP devices; unknown/missing IDs remain unknown rather than inferred.
- Parts Inventory now has one canonical launch action to the dedicated Parts Inventory workspace. The older Workshop inventory view and its legacy code path remain in place; no inventory records or WorkshopStore data were removed.
- The catalog contains 52 unique executable ActionIds. All 52 have explicit MainWindow handlers; no repeated ActionId remains in the feature rows.
- A total of 84 pull-request files were reviewed; none had status `removed`. This is a navigation correction, not a deletion of old tool implementations.
- Latest CI run #257 passed 150/150 unit tests, Windows x64 self-contained publishing, Android Agent build/upload and offline tool bundling: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44779995644
