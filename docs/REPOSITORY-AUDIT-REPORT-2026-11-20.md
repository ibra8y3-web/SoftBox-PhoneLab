# SoftBox Repository Audit Report — 20 November 2026

## Audit scope and baseline

- Repository: `ibra8y3-web/SoftBox-PhoneLab`.
- Product identity retained: SoftBox — Universal Smart Device Service & Repair Platform.
- Main baseline used to create the additive development branch: `12650bed442498f7746432de42bea95b1a622381`.
- Working branch: `feature/apk-lab-real-apk-inspector`.
- Pull request: https://github.com/ibra8y3-web/SoftBox-PhoneLab/pull/1
- This report is a repository/code/catalog/CI audit. It is not a claim that every feature was exercised on physical hardware.

## Repository and architecture found

- The existing application is a Windows WPF desktop application on .NET 10 with a Kotlin Android companion Agent.
- The existing .NET solution contains Core, Desktop and Tests projects, with the Android Gradle project maintained separately.
- A single `SoftBoxModuleCatalog` defines the 36 product sections, their feature inventory, implementation states and executable action IDs.
- `ModuleSectionWindow` presents a consistent section experience; an executable button is shown only when a feature has a real ActionId.
- Existing ADB/device discovery, shared device information, Agent-based device tests, USB/Fastboot inspection, screen mirroring, firmware inspection and workshop/inventory data were retained.
- The workshop data store is local JSON under the current Windows user's LocalAppData with an existing v1/v2 migration path. SQLite and EF Core were not present and were not introduced as an unrelated migration.
- The desktop UI has working WPF code-behind and services but is not yet a complete MVVM/Clean Architecture implementation. Application-wide DI, structured logging, plugin host, signed updater and user-account system remain future work.

## Feature inventory snapshot

| Inventory | Count |
|---|---:|
| Top-level sections | 36 |
| Sections In Development | 22 |
| Sections Planned | 14 |
| Catalog entries including sections and child features | 151 |
| Implemented entries | 58 |
| Tested entries | 5 |
| In Development entries | 23 |
| Planned entries | 63 |
| Unsupported entries | 2 |
| Verified entries | 0 |

These are catalog status counts, not a percentage of product completion. `Verified` remains zero because CI and generated test fixtures do not establish physical-phone or vendor-specific hardware compatibility.

## Implemented in this change set

### Section 05 — APK Lab

- Added local, read-only APK inspection: full-file SHA-256, reading non-directory ZIP entries, text XML and Android Binary XML manifest parsing, package/version/SDK/permission extraction, structural discovery of v1 signing metadata and APK Signing Block, and two-file metadata/permission/archive-path comparison.
- Added a WPF workspace and wired it to the existing APK Lab section.
- Added automated tests covering text/binary manifests, metadata extraction, SHA-256, signature-file discovery, missing manifest, corrupt ZIP, input validation, two-file version/permission/path comparison and mismatched package warnings.
- Cryptographic certificate/signature verification, Android resource resolution and DEX analysis remain Planned. The two-file APK metadata/permission/path comparison is implemented and tested.

### Section 16 — Network Diagnostics

- Added local Windows network-interface inventory with operational state, adapter type, IPv4/IPv6 addresses, gateways and DNS server addresses.
- Added DNS resolution and a single ICMP echo probe to one user-selected host/IP, with bounded timeout and cancellation.
- Added a WPF workspace and wired it to the existing Network Diagnostics section.
- Added tests for accepted DNS/IP inputs, short local hostnames and rejection of URLs/paths/malformed values.
- Android-private network observations and ADB Wireless pairing remain Planned. The tests do not make external DNS or live ICMP requests.

## Preservation and reorganization

- No existing project, service, module or tool implementation was deleted.
- Existing product name, project names, namespaces, WPF/.NET 10 stack and Android Agent were retained.
- Existing tools were not moved: the audited catalog already gives each tool a canonical section/action and shared code path; a speculative relocation risked duplicate navigation or broken references.
- The two new capabilities were added to their intended existing sections (05 and 16), not as replacement modules.
- No new NuGet or third-party runtime dependencies were introduced.

## Build and test evidence

- PhoneLab CI run #57: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44753337114
- Result: Success.
- Restore: passed.
- Unit tests: 51 passed, 0 failed, 0 skipped.
- Windows self-contained x64 publish: passed.
- Android Agent Gradle build and artifact upload: passed.
- Offline USB, scrcpy and TRACE Boardviewer bundle checks: passed.
- Windows package artifact (227,161,431 bytes): https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44753337114/artifacts/14637025549
- A prior APK-only verification run #48 passed 40/40 tests; run #57 includes both new modules and is the latest source-code verification.

## Known issues and unverified scope

- No physical Android handset, USB chipset, Download Mode, Qualcomm EDL, MediaTek Preloader/BROM, firmware flash or board-level fixture test was performed during this change set.
- ADB/fastboot coverage remains dependent on device authorization, drivers and the actual mode/protocol supported by the handset.
- APK signature metadata discovery is not cryptographic signature verification or malware analysis.
- Network diagnostics currently reads the Windows host. It does not claim to read private Android Wi-Fi information; ICMP may be blocked independently of DNS.
- Planned sections remain Planned; partial sections retain their In Development classification. See `SOFTBOX-MASTER-MODULE-STATUS.md` for each of the 36 section-by-section gaps.
- The code was built and tested by GitHub's Windows CI runner. The current Linux execution environment did not provide a local Windows/WPF runtime, so no local desktop launch or screenshot test is claimed.

## Final repository state

- Changes are on the feature branch and submitted as Pull Request #1.
- Main was not modified directly and the pull request remains open for review.
- No release or permanent GitHub Release was created. The Windows ZIP linked above is a temporary Actions artifact.
## Final verification after APK comparison — CI run #65

- Result: Success on the pull-request source/catalog containing the two-file APK comparison.
- Unit tests: 53 passed, 0 failed, 0 skipped.
- Windows x64 self-contained publish, Android Agent build, offline tool bundling and artifact upload: passed.
- Run: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44754536968
- Windows artifact (227,167,411 bytes): https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44754536968/artifacts/14636852748

This is CI verification using generated local test fixtures and a Windows runner. It is not a physical-handset compatibility claim.
## Workshop inventory integration — additive implementation

- Workshop schema upgraded from version 2 to 3 while preserving existing customers, repair orders and inventory records.
- Added per-order parts usage snapshots and calculated parts cost.
- Added inventory movement ledger with delta, resulting balance, unit cost, reason, technician and optional repair-order/ticket linkage.
- Stock consumption is performed under the WorkshopStore lock and committed through one atomic JSON replacement; insufficient stock, delivered orders and cancelled orders are rejected.
- Global workshop repair-order editor exposes a parts-consumption dialog; the advanced Parts Inventory window manages category, supplier, cost price, sale price, reorder threshold, manual adjustments and CSV export.
- Repair-order text, HTML and CSV reports now include used parts and parts cost.
- Added tests for successful consumption, insufficient stock protection and schema-v2 migration.
## Workshop Parts Inventory and repair-order integration

- Upgraded the local workshop JSON schema from version 2 to version 3, preserving existing customers, repair orders and inventory entries.
- Added category, supplier and sale-price metadata to inventory items.
- Added an inventory movement ledger with signed quantity delta, resulting balance, unit cost, reason, technician and optional repair-order/ticket linkage.
- Added a repair-order parts usage snapshot and calculated parts-cost total.
- Added a single locked store operation to consume a part against an order; insufficient stock, delivered orders and cancelled orders are rejected before the write.
- Added an order-linked parts usage dialog, advanced parts catalog editor, low-stock summary and CSV exports for inventory and movements.
- Repair-order text/HTML/CSV reports include parts usage and parts cost.
- Added integration tests for stock deduction and order linkage, insufficient stock protection and schema-v2 migration.