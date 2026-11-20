# SoftBox Reorganization Report — 20 November 2026

## Result

No pre-existing tool was moved in this pass. The repository already had a 36-section sidebar/catalog and a canonical `ActionId` mapping for existing tools. The shared `ModuleSectionWindow` displays an executable button only for a feature with a wired action. Moving tools without a demonstrated ownership conflict would risk duplicate entries, broken shortcuts or duplicated implementations.

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

- `src/SoftBox.PhoneLab.Core/Models/SoftBoxModuleCatalog.cs`: added the two feature inventories and their real action IDs; existing action IDs and section IDs were retained.
- `src/SoftBox.PhoneLab.Desktop/MainWindow.xaml.cs`: routed the two new action IDs to their dedicated WPF windows.
- No old action ID was removed and no old tool implementation was relocated or duplicated.

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