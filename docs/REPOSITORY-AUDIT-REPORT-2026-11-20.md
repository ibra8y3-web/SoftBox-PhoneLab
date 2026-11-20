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

- The additive changes were integrated into `main` through Pull Request #1.
- Merge commit: `137be00700785b3409f71e2400870f8c6d73db10`.
- Pull Request #1 is closed as merged; its 84 changed files include **0 deletions**.
- The final pre-merge CI on the exact PR head (run #259) passed: 150 tests, 0 failures, 0 skipped; Windows x64 publish, Android Agent build/upload and offline tool bundling passed.
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
## Vendor-specific diagnostics — additive implementation

- Added a fixed allow-list of Android `getprop` keys for Samsung (One UI, CSC, PDA, bootloader, Verified Boot), Xiaomi/Redmi/POCO (model, market name, MIUI/HyperOS version, region, hardware country, Verified Boot), and generic Android brands.
- Vendor diagnostics require an authorized ADB device and do not accept arbitrary property names or shell commands from the user.
- Empty or unavailable properties are reported as unavailable rather than replaced with guessed values.
- Reports can be saved locally as JSON with a masked device serial.
- No Download Mode, EDL/BROM protocol, bootloader unlock, FRP, security bypass or flashing action was added.
## Smart Backup authenticated encryption

- Added a streaming `.sbx` container using AES-256-CBC encryption with encrypt-then-HMAC-SHA256 authentication and PBKDF2-HMAC-SHA256 (600,000 iterations, random 16-byte salt and IV).
- Authentication is checked before any plaintext is written during decryption.
- Android shared-storage backups can be created directly as encrypted SBX: ZIP is built in a temporary directory, encrypted, then the staging directory is removed.
- Encrypted verification decrypts only to a temporary ZIP, re-runs the per-file SHA-256 Manifest checks, then removes the temporary plaintext archive.
- Passwords are not stored in settings or operation logs. The legacy ordinary ZIP option remains available and is explicitly marked unencrypted.
- Automated tests cover round-trip restore, plaintext absence in the encrypted bytes, wrong-password rejection, tamper rejection and password policy.
## Plugin SDK and safe package inspection

- Added versioned Plugin Manifest v1 contracts with semantic-version validation and a closed permission allow-list.
- Added local ZIP package inspection with package SHA-256, path/duplicate/symlink checks, size and expansion limits, compression-ratio checks, manifest validation and entry-assembly existence checks.
- Added a local Plugin Store inspection window and sample manifest/developer quickstart.
- Plugin installation/execution remains disabled: no trusted publisher root/signature verifier, sandboxed runtime, IPC host or permission-grant/revocation lifecycle is implemented. Inspection always returns `PublisherSignatureVerified=false` and `CodeLoadedOrExecuted=false`.
## Android backup restore

- Added a restore path for ordinary ZIP and authenticated SBX backups. ZIP Manifest/per-file SHA-256 verification or SBX HMAC authentication plus inner Manifest verification runs before files are sent to the phone.
- Files are restored into a newly generated `SoftBox-Restore-*` subdirectory under the user-selected public shared-storage parent; existing phone files and original folders are never deleted or overwritten by this restore flow.
- Archive paths are validated against traversal, absolute paths, Windows-incompatible names and reserved device names before local staging. Staging files are removed after transfer.
- Per-file transfer failures are reported; a connection interruption may leave partial files in the new restore directory. No destructive rollback is attempted.
- Added unit tests for safe/unsafe restore paths. Real-phone restore and OEM storage permissions still require hardware validation.
## Phone Link Studio — screen recording

- Added a user-driven recording window for the selected ADB-authorized Android device.
- Reuses the existing shared ScreenMirrorService and verified scrcpy binary; recording uses the scrcpy `--record` argument and supports MP4/MKV output chosen by the user.
- Stop sends WM_CLOSE to the scrcpy window, waits for process exit and confirms that the output file exists and has non-zero length. This does not prove every codec/audio combination on every handset; physical device validation remains pending.
- The recording is the Android device stream, not a capture of the Windows desktop. Ending the recording also ends the current scrcpy mirror session.
## Phone Link recording and Safe Recovery preflight

- Phone Link Studio now exposes a real scrcpy `--record` path for MP4/MKV recording, asks for a new output path and finalizes capture using a graceful window close before validating the output file.
- Recording is launched only for the selected authorized ADB device. It stops the current embedded scrcpy session; multi-session recording remains out of scope.
- Firmware Center now includes an inspect-only Safe Recovery preflight. It re-hashes the selected local firmware immediately before evaluating it and compares manufacturer/model text against the selected authorized device snapshot.
- The preflight reports explicit Passed/Failed/Unknown/Blocked evidence and always denies proceeding while publisher authenticity, signed device compatibility, anti-rollback policy or verified backup evidence is unavailable. No flashing action is enabled.
- Automated tests cover matching local hash with trust blockers, model/manufacturer mismatch and missing authorized device identity.
## Supplier purchase receipt workflow — Workshop schema v4

- Added a supplier purchase receipt ledger to workshop data schema v4; migration upgrades existing schema 1–3 JSON records without discarding customers, repair orders, inventory or movement history.
- Added stock receipt for an existing SKU, including supplier, invoice reference, quantity, purchase unit cost and receiving technician.
- Receipts update stock quantity and weighted-average unit cost and create a matching positive inventory movement. The operation is serialized and persisted in one store write; in-memory changes roll back if persistence fails.
- Parts Inventory now provides supplier receipt entry and CSV export for receipt history. Multi-line purchase orders, payment tracking and approval workflows remain unimplemented.
- Integration tests cover weighted-average cost, stock balance, invoice linkage, invalid supplier rejection and v2-to-v4 migration.
## Android Fastboot manager

- Added `FastbootService`, which locates the official Platform-Tools `fastboot.exe` beside the configured ADB executable, in the bundled Tools directory or PATH.
- Device discovery parses Fastboot-only rows and validates the device serial before exposing it to the UI.
- The manager reads `fastboot getvar all` for the selected serial and provides a user-confirmed `fastboot reboot` to Android.
- The service intentionally exposes no arbitrary command execution, flash, unlock, erase, format or boot operations.
- Added unit tests for Fastboot device parsing, deduplication and rejection of unsafe/non-Fastboot entries.

## Current additive development branch — catalog after navigation cleanup

This is the current pull-request branch snapshot, not the pre-implementation `main` baseline counts shown earlier in this report.

| Inventory | Count |
|---|---:|
| Existing top-level sections retained | 36 |
| Sections In Development | 35 |
| Sections Planned | 1 |
| Child feature entries | 129 |
| Catalog entries including sections and child features | 165 |
| Planned entries (including section rows) | 24 |
| In Development entries (including section rows) | 38 |
| Implemented entries | 95 |
| Tested entries | 6 |
| Verified entries | 0 |
| Unsupported entries | 2 |
| Unique executable ActionIds | 52 |
| Duplicate executable ActionIds | 0 |
| Executable ActionIds with no MainWindow route | 0 |

The current catalog statuses are implementation classifications, not a completion percentage. All top-level sections remain In Development except User & Technician Management, which remains Planned. The zero Verified count is intentional: the current hosted CI does not prove physical-device or vendor-protocol compatibility.

## Navigation and dedicated tool entrypoints

- Android file manager, app manager, read-only command center and batch operations each enter the appropriate tab within one shared ADB-authorized Android workspace.
- Qualcomm and MediaTek open the common USB Doctor with actual Windows PnP filters for the common 05C6:9008 and 0E8D:0003 USB identity hints. This does not claim EDL/BROM protocol communication, programmer/DA compatibility or flashing.
- Parts Inventory feature records now point to one canonical Parts Inventory window. Workshop Manager's legacy inventory entry/view and the shared WorkshopStore remain intact.
- Duplicate feature-row launch aliases were removed only where they opened the exact same shared window. No feature implementation, source file, section, data store or user data was deleted.
- The pull request contains no files with Git status `removed`.

## Latest verification — CI run #257

- Commit: `55ae843048a988b5883af0cb8953a4eab741f322`.
- Result: Success — Restore passed; **150 tests passed, 0 failed, 0 skipped**; Windows self-contained x64 publish passed; Android Agent Gradle build/upload passed; offline tool bundling passed; Windows artifact uploaded.
- Windows artifact (227,398,184 bytes): https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44779995644/artifacts/14652201449
- No CS* compiler or xUnit analyzer warnings appeared in the latest run logs.
- No physical Android handset, Qualcomm/MediaTek USB interface, firmware flash or board-level fixture was tested by this CI run.


## Final documentation revision — CI run #258

- The final report set was committed on the feature branch at `526c776e6bb28e7a4f7e7e141e02bac3f0fc879b`.
- CI run #258 completed successfully: 150 passed, 0 failed, 0 skipped; Restore, Windows x64 self-contained publish, Android Agent build/upload, offline tool packaging and artifact upload passed.
- Artifact (227,398,186 bytes): https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44780672062/artifacts/14652362018
- The feature branch remained in its pull request for integration into `main` after this successful check; no hardware verification is implied.


## Follow-up PR #2 — User & Technician Management

- Pull request: https://github.com/ibra8y3-web/SoftBox-PhoneLab/pull/2
- Branch: `feature/user-technician-management`.
- The previously Planned section 30 now contains real local account bootstrap/authentication, three fixed roles, PBKDF2 password verifiers, lockout, account administration, session re-authentication and a local audit trail.
- Authorization is fail-closed for all 53 catalog action IDs. Module launch is recorded against the authenticated user. Read-only roles cannot invoke interactive screen control or device-write/workshop/admin actions.
- Account data is stored within the current Windows user's LocalAppData profile. This is not domain identity, Windows Hello, dedicated ACL/DPAPI enforcement, or a central multi-workstation identity platform.
- PR #2 CI run #273 passed **166 tests** (0 failures, 0 skipped), Restore, Windows x64 publish, Android Agent build and offline bundle packaging: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44786097393
- Windows x64 artifact (227,451,055 bytes): https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44786097393/artifacts/14653329474
- The final PR #2 head records each permitted module-action invocation against the authenticated user and denies launching an action if its audit entry cannot be written. The ReadOnly role can run allow-listed read-only command/workflow/batch inspections but not interactive screen control; auto mirror and Agent installation are disabled for read-only sessions.
- PR #2 was merged into main through merge commit `137be00700785b3409f71e2400870f8c6d73db10`. Its latest CI #274 passed 166 tests, Windows x64 publish, Android Agent build and offline package validation.



## PR #3 — Offline Readiness Center

- Pull request: https://github.com/ibra8y3-web/SoftBox-PhoneLab/pull/3
- Extends the existing Offline Center section 28 with a real read-only inventory of the seven expected application bundle files plus local device-profile and board-map directories.
- Reports actual file presence, readability, size, modified time, executable version metadata and SHA-256. SHA-256 is a content fingerprint, not a trusted publisher signature or a pre-approved source hash.
- Reports current OS/runtime, active network interfaces and installation-volume free space without claiming Internet access. A user-triggered JSON export remains local.
- Rejects reparse points and fails closed when link status cannot be established; directory scans skip reparse points and stop after 10,000 files.
- PR #3 CI run #279 passed **171 tests**, Restore, Windows x64 publish, Android Agent build/upload, offline dependency bundling and artifact upload: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44788760474
- Windows artifact (227,459,925 bytes): https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44788760474/artifacts/14654323613


## PR #3 current implementation state

The Offline Readiness feature has been added to the existing section 28 and classified Tested after CI #279. The overall section remains In Development because the larger Offline Center scope (Internet reachability, trusted source hashes/signatures, package repair/install and full local dependency inventory) is not implemented.

No hardware compatibility is claimed. No package is downloaded or installed by the Offline Center. The local SHA-256 shown is computed from the current file bytes only.
