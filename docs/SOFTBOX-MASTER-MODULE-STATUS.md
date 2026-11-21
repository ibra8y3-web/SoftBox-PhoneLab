# SoftBox Master Module Audit and Development Roadmap

Audit baseline: pre-implementation `main` snapshot, commit `12650bed442498f7746432de42bea95b1a622381`, 20 November 2026. APK Lab changes are recorded on `feature/apk-lab-real-apk-inspector` until CI review is complete.

This document maps the supplied **SoftBox – Universal Smart Device Service & Repair Platform** specification to the code that already exists. It is an implementation inventory, not a claim that every module is complete.

### Pre-implementation main snapshot

- Top-level sections: 36 total — 22 In Development and 14 Planned.
- Catalog entries: 151 total (36 sections plus 115 child features): 58 Implemented, 5 Tested, 63 Planned, 23 In Development and 2 Unsupported.
- No item is marked Verified in this snapshot. A successful Windows CI run is not physical-device verification.

### Current additive branch snapshot — encryption and dashboard analytics follow-up

- Top-level sections: **36 retained** — all 36 In Development.
- Child features: **130**; catalog entries including section rows: **166**.
- Current feature statuses (child features only): **14 Planned, 4 In Development, 96 Implemented, 14 Tested, 0 Verified and 2 Unsupported**.
- Including 36 section rows (all In Development): **14 Planned, 40 In Development, 96 Implemented, 14 Tested, 0 Verified and 2 Unsupported**.
- Executable navigation: **56 unique ActionIds**, all routed through the central dispatcher and explicit fail-closed authorization policy; no duplicate executable action IDs.
- The module catalog still contains **no Verified entries**; hosted CI validates code paths but does not constitute physical-device verification.

## Status definitions

- **Planned**: no working implementation yet.
- **In Development**: some real pieces exist, but the module is incomplete.
- **Implemented**: the described function has real code. It may still require physical-device verification.
- **Tested**: tests cover the function in the stated test environment.
- **Verified**: verified in the intended real environment with recorded evidence.
- **Unsupported**: intentionally not provided in the current product scope.

A green build does not make a hardware feature Verified. Unit tests do not substitute for tests on a real handset, USB chipset, driver, fixture or firmware package. Navigation pages read their state from `SoftBoxModuleCatalog`; an unfinished feature never receives an action button.

## The 36 product sections

| # | Section | Current status | Already present | Important missing work |
|---:|---|---|---|---|
| 01 | Dashboard | In Development | Live ADB availability, connected-device selection, persisted device history and notifications; Tested analytics over recorded devices/events/notification categories, 24h/7d/30d counters and 14-day activity trend | A durable background job queue, storage health and signed update-health aggregation |
| 02 | Device Center | In Development | ADB discovery, multiple-device listing and selection; serial-scoped operations; local device connect/disconnect/authorization history; real Wireless debugging pairing via adb pair/connect with separate IP/port endpoints and one-time code; protocol tests passed | Persistent device sessions, group targeting and independent background job queue; no physical-phone wireless pairing verification yet |
| 03 | Device Intelligence | In Development | Android property parsing, model-profile matching, community model-code catalog, verified board-map import rules | Per-model capability matrix, richer provenance, protocol/driver compatibility and reviewed device coverage |
| 04 | Android Service Center | In Development | Authorized ADB inspection, local Agent bridge, shared-storage browser/transfers, third-party App Manager and Runtime permission actions, fixed read-only command presets, bounded local Logcat failure analysis, Fastboot getvar and confirmed System/Recovery/Fastbootd transitions | Free-form shell, private app-data access, model-specific destructive Fastboot servicing and hardware-proven compatibility remain unsupported |
| 05 | APK Lab | In Development | Local APK Lab window; SHA-256; readability of every non-directory ZIP entry; text XML and Android Binary XML manifest parsing; permission/version extraction; structural signature metadata detection | Cryptographic signature/certificate validation, resource resolution and DEX/resource analysis; no installation or device action |
| 06 | Samsung Tools | In Development | Official Samsung USB driver; fixed-key One UI/CSC/PDA/bootloader/Verified Boot reports; Samsung-only ADB Download Mode entry after manufacturer re-check; Odin Assistant validates BL/AP/CP/CSC or HOME_CSC selection and hashes packages before opening a user-provided external Odin executable | SoftBox does not automate Odin controls or claim a completed flash; exact firmware source, model/region and anti-rollback verification remain required |
| 07 | Xiaomi Tools | In Development | Xiaomi/Redmi/POCO fixed-key getprop collection for model/build/MIUI-HyperOS/region/Verified Boot with JSON report | Model/region compatibility database and official model-specific Fastboot/recovery workflows |
| 08 | Android Brands | In Development | Generic read-only Android manufacturer/brand/model/build and Verified Boot property collection | Dedicated, evidenced service protocols for Pixel, Motorola, OnePlus, OPPO, realme, vivo, Huawei, Honor, TECNO, Infinix and others |
| 09 | Qualcomm Tools | In Development | SoC identification hints plus an automatically opened Windows PnP view filtered to the common 05C6:9008 EDL USB identity hint | EDL protocol, programmer verification, secure-boot compatibility and signed model-specific recovery (not implemented) |
| 10 | MediaTek Tools | In Development | SoC hints plus an automatically opened Windows PnP view filtered to the common 0E8D:0003 Preloader/BROM USB identity hint | BROM protocol, Download Agent verification, secure-boot compatibility and signed model-specific recovery (not implemented) |
| 11 | Firmware Center | In Development | Local firmware import/catalogue by vendor/model/region/version, file SHA-256, ZIP/OTA archive inspection and post-import integrity revalidation | Trusted source catalog, signed publisher verification, download manager and device/model/region/rollback compatibility gate |
| 12 | USB Doctor | In Development | ADB/Fastboot transport inspection; Windows SetupAPI PnP inventory with VID/PID, friendly name, service and problem code; conservative Samsung/Google/Qualcomm/MediaTek mode hints; official Samsung driver entry | Driver package signature verification, controlled install/remove/rollback and OEM driver catalogue; no driver mutation in the current PnP inspector |
| 13 | Smart Backup | In Development | ADB shared-storage backup into ZIP with per-file SHA-256; authenticated SBX encryption; verified restore to a unique shared-storage directory; DPAPI-protected schedules for on-connect, daily and weekly encrypted backups; 30-second authorized-device polling while SoftBox is open; pause, resume, delete and run-now controls | Scheduling does not run after SoftBox exits or the Windows user signs out; app-private/system data backup remains unsupported by design |
| 14 | Phone Link Studio | In Development | Verified scrcpy screen mirroring/control; real MP4/MKV screen recording is wired to the selected authorized ADB device, requires user-chosen new output path and finalizes via WM_CLOSE before validating non-empty output file | Hardware recording validation across Android versions/codecs, multi-session concurrent display and app-side recording library management |
| 15 | Hardware Diagnostics | In Development | Agent screen/touch/audio/microphone/vibration/sensor checks, Camera2 routes, screen-photo analysis and external meter readings | More model-tested tests, durable per-session evidence, calibrated lab procedures and supported board-level fixtures |
| 16 | Network Diagnostics | In Development | Local Windows interface inventory, DNS resolution and one user-selected ICMP probe; authorized Android interface-address and IP-route observations through ADB with bounded timeouts/cancellation | Wi-Fi radio/security metadata, internet reachability from the phone and model-specific connectivity diagnostics remain unimplemented; no phone-private or unavailable data claim |
| 17 | Smart Repair AI | In Development | Deterministic local evidence-linked advisor over battery/storage readings, Agent results and component diagnostics with severity, confidence, evidence and safe next steps; external repair guide search | LLM-backed online mode and expanded model-specific offline repair knowledge; current local advisor is rules-based, not an AI model |
| 18 | One-Click Diagnostic | In Development | One action runs five read-only ADB presets (properties, battery, storage, uptime, third-party apps), supports cancellation and saves a device-masked JSON report | Coordinated opt-in hardware test suite and consolidated lab instrumentation/Agent evidence |
| 19 | Safe Recovery | In Development | Conservative preflight is wired into Firmware Center; re-hashes selected file, compares exact manufacturer/model against the current authorized device snapshot, and reports Passed/Failed/Unknown/Blocked with evidence; automated tests enforce never-allow while publisher trust, signed compatibility rules, rollback and verified-backup proof are unavailable | Official publisher trust roots, signed model/region/board compatibility database, anti-rollback policy evidence and vendor-authorized recovery workflow; no flashing is enabled |
| 20 | Workflow Automation | In Development | JSON workflow builder/save/load and sequential read-only ADB runner with allow-list validation, cancellation and step-level results | Conditions, triggers, safe retry policies and broader workflow actions beyond read-only presets |
| 21 | Batch Operations | In Development | Multi-device selection and bounded concurrent read-only ADB preset execution with per-device results and configured concurrency limit | Additional allowed batch actions, independent per-device cancellation and durable batch job queue |
| 22 | Repair History | In Development | Persistent local per-device connection/authorization/inspection/Agent/report history using SHA-256 device fingerprint and masked serial; existing workshop event/timeline history retained | Broader linkage of every service action and diagnostic result, searchable retention/cleanup and encrypted local store |
| 23 | Workshop Manager | In Development | Customers, repair orders, status tracking, QR ticket, printable intake receipts/estimates/invoices, manual quote approval/rejection history, multi-payment ledger, remaining balance and printable payment receipts | External payment settlement/refunds, tax calculation/compliant e-invoicing, intake accessory checklist, photos with consent and multi-user workflow |
| 24 | Parts Inventory | In Development | SKU catalogue with category/supplier/cost/sale/reorder fields in the dedicated Parts Inventory window; supplier stock receipts record supplier and invoice, quantity and purchase unit cost, update weighted-average cost, and atomically add a purchase receipt plus stock movement; order-linked consumption deducts stock and records parts cost; inventory/movement/purchase CSV exports | Multi-line purchase orders, invoice approval/payment tracking, barcode scanning and multi-branch synchronization |
| 25 | Service Reports | In Development | Device JSON diagnostics; repair-order HTML/text/UTF-8 CSV; actual workshop analytics for delivered revenue, open estimates, reported symptom descriptions, serviced models, technicians, delivery duration and inventory value; analytics CSV export | Native PDF/Excel, signed report provenance and deeper financial analytics; current values are operating indicators, not profit or tax accounting |
| 26 | Plugin Store | In Development | Local ZIP/Manifest inspection, permission allow-list validation, Zip Slip/symlink/duplicate-path/size/ratio checks and package SHA-256; install and code execution remain disabled | Publisher signature/trusted-root verification, permission grant UI, dependency manager, install/update/uninstall lifecycle and sandboxed runtime |
| 27 | Plugin SDK | In Development | Version-1 manifest contract, semantic version validation, known permission identifiers, fail-closed unknown permissions, sample manifest and developer quickstart | Public SDK assembly/contracts, actual host API, runtime IPC, sample executable plugin and compatibility test host |
| 28 | Offline Center | In Development | Read-only local package inventory for the SoftBox EXE, ADB, Fastboot, scrcpy, Android Agent APK, Samsung USB Driver and TRACE Boardviewer; actual presence/missing/unreadable state, file size, modified time, executable version metadata and SHA-256; actual file counts and aggregate sizes in device-profile and board-map directories; OS/runtime/network-interface/storage inventory; JSON export; link/Junction paths rejected | Internet reachability validation, expected-source hash/signature verification, package repair/download/install and broader local component discovery remain unimplemented |
| 29 | Security & Privacy | In Development | ADB argument-list invocation and serial validation; allow-listed read-only shell presets; Agent consent; safe public-storage paths; secret-redacted local support ZIP; masked device history identifiers; DPAPI CurrentUser encryption for account/audit, workshop, activity and notification stores with legacy JSON migration and same-user encrypted workshop backups | Portable cross-account key recovery/sync, signed updater/plugin trust roots, full OS ACL hardening and formal threat model |
| 30 | User & Technician Management | In Development | First-run administrator bootstrap; mandatory local sign-in; administrator/technician/read-only roles; PBKDF2-HMAC-SHA256 password verifiers; DPAPI-protected account/verifier/audit store with legacy JSON migration; password lockout; account administration; self-service password change; manual session lock/re-authentication; bounded audit | Windows Hello/identity integration; unattended idle timeout; custom per-user permission editor; password recovery policy; full attribution of every internal WorkshopStore mutation and all nested device-operation outcomes; central multi-workstation identity |
| 31 | Update Center | In Development | Official GitHub Stable/Beta release metadata inspection and local device-catalog refresh | Signed application update manifest, trusted publisher roots, secure staged download/install, rollback and release-channel enforcement |
| 32 | Notification Center | In Development | Persistent local inbox for device connect/disconnect/ADB authorization, Agent diagnostics and report export; unread/read and mark-all-read | Additional operation-completion sources, filtering and direct deep links for every module event |
| 33 | Help Center | In Development | Searchable local Arabic help articles with English titles/keywords for ADB, USB, APK, backup, firmware, network, workflow, support and privacy limitations | Expanded illustrated guides, full glossary and manufacturer/model-specific troubleshooting articles |
| 34 | Support & Logs | In Development | Bounded Android evidence collection, firmware inspection reports, local support ZIP with system/runtime metadata and user-selected TXT/LOG/JSON files plus password/token/authorization/device-ID redaction | Structured application log pipeline, crash capture and stronger review of non-standard secret formats |
| 35 | Settings | In Development | Persisted local preferences for ADB discovery interval, screen auto-open, Agent auto-setup, auto-inspection and batch concurrency; workshop currency/locale/time-zone settings remain | App-wide theme, language, keyboard shortcuts, privacy/data retention and storage settings |
| 36 | About | In Development | Application/Assembly version, runtime, OS/architecture and bundled THIRD-PARTY-NOTICES display | Build provenance, complete dependency/license inventory and official support links |

## Existing tools and their canonical sections

The same implementation is shared and linked from related module pages; it must not be copied into a second implementation. Each existing tool has one canonical executable navigation action; related modules describe the dependency without adding a second Run button.

| Existing implementation | Canonical section | Notes |
|---|---|---|
| ADB auto-discovery and connected device list | Device Center | ADB authorization is required; MTP does not count as ADB |
| Device property parser and community profile matcher | Device Intelligence | A match is an identity hint, not proof of PCB revision |
| Agent bridge and on-device tests | Android Service Center / Hardware Diagnostics | Requires a real Android device and explicit on-device consent |
| scrcpy embedded screen and control | Phone Link Studio | Hardware behavior is not proven by compile-only CI |
| Camera2 and physical screen-photo analysis | Hardware Diagnostics | Screening aids, not calibrated optical certification |
| External serial meter dashboard | Hardware Diagnostics | Read-only JSON-Lines protocol; fixture safety/calibration is external |
| ADB/Fastboot transport inspector and Samsung driver installer | USB Doctor | Generic discovery cannot promise visibility of every vendor-specific mode |
| Local firmware archive inspection | Firmware Center | Hash/archive structure is not publisher-signature or compatibility verification |
| Local APK archive and Manifest inspection | APK Lab | SHA-256, ZIP entry readability, text/AXML metadata and signature-structure discovery; does not validate publisher trust |
| Windows host network interface and DNS/ICMP probe | Network Diagnostics | Reads local interface properties; probes only one explicitly selected host and does not inspect Android-private Wi-Fi data |
| TRACE Boardviewer launcher and verified JSON board-map importer | Device Intelligence | Boardviewer is a separate third-party app; native formats are not decoded into PhoneLab 3D |
| Customer/order/inventory local store | Workshop Manager / Parts Inventory | One Windows workstation, JSON storage, no multi-branch synchronization |
| Workshop international settings, warranty and printable reports | Workshop Manager / Service Reports | Currency code formats labels; there is no FX conversion or automatic tax calculation |
| External iFixit guide search | Smart Repair AI (related knowledge only) | This is not an AI repair assistant |

## Engineering baseline found in the repository

- Windows UI: WPF on .NET 10; Android companion: Kotlin/Android Agent.
- Current solution has three .NET projects: Core, Desktop and Tests, plus the separate Android Gradle project.
- ADB process invocation uses argument-list APIs, validates serial values and has a bounded command timeout.
- APK Lab uses a dependency-free .NET Core inspection service for local ZIP entry-read checks, SHA-256 and text/Android Binary XML manifest parsing; signature data is detected structurally only.
- Network Diagnostics reads local Windows interface configuration and performs one user-selected DNS/ICMP probe with bounded timeouts; Android-private network observations are still unimplemented.
- The workshop database is local JSON under the current Windows user's LocalAppData. It has a schema migration path for its v1/v2 workshop schema.
- SQLite and Entity Framework Core are **not** currently used.
- The current desktop UI has meaningful code-behind and services, but it is **not yet a complete MVVM/Clean Architecture implementation**.
- A full application-wide dependency injection container, centralized structured logging, user accounts, plugin host, signed updater and app-wide preferences are not implemented.
- Do not rename or split projects just to imitate the suggested architecture. Introduce boundaries incrementally with tests and migrations.

## Implementation order after the module shell

1. **Navigation and module inventory**: the 36 sections, one canonical catalog, dedicated section windows, correct links to existing tools and an audited status for every feature.
2. **Stage 1 foundation**: real dashboard data, application settings, structured logs, device/session models, device engine boundaries, persistent storage decision and multi-device-safe session IDs.
3. **Stage 2 Android**: ADB manager, Fastboot manager, command catalog, file manager and app manager, each with allow-lists, per-device targeting and integration tests.
4. **Stage 3 USB and diagnostics**: USB Doctor, Windows driver health, network diagnostics and coordinated One-Click Diagnostic.
5. **Stage 4 backup and Phone Link**: encrypted file backup, restore validation, scheduling and recording/multi-session controls.
6. **Stage 5 brand/platform providers**: Samsung, Xiaomi, other Android brands, Qualcomm and MediaTek, based only on verified protocols and model support.
7. **Stage 6 firmware and recovery**: trusted firmware sources, resumable downloads, compatibility checks and central Safe Recovery gates.
8. **Stage 7 intelligence and history**: evidence-linked repair assistant, unified Repair History and safe workflow/batch orchestration.
9. **Stage 8 plugins**: contracts, isolated host, signature and permission policy, SDK and store.
10. **Stage 9 global release**: offline center, updater, localization, signing, security review, installers and hardware certification.

## Rules for adding or moving a tool

1. Search the current code and this matrix before adding anything.
2. Keep one implementation/service for one tool. Link it from a relevant section when necessary; do not duplicate the implementation.
3. If a tool belongs in another section, move its navigation entry and update the matrix. Preserve its service and tests.
4. Every new module or significant tool needs a stable ID, owner section, status, real action, error handling and tests appropriate to its environment.
5. Do not display an executable action for Planned or Unsupported features.
6. Set a feature to Tested only when an automated test actually covers it. Set it to Verified only after the target environment/device test is recorded.
7. Keep customer data local by default; request consent for exports and remote services.
8. Build and test after each stage; do not start the next dependent stage with a broken main branch.


## Current navigation ownership after cleanup

The latest catalog is audited at **55 unique executable actions**. Android file, app, read-only command and batch features launch separate tabs in the same authorized-device workspace. Qualcomm and MediaTek launch the shared USB Doctor with live Windows PnP filtering for their common VID/PID identity hints. Parts Inventory has one canonical window/action while the existing Workshop inventory view remains intact. Section 30 now provides local user accounts and role-based tool authorization; all catalog actions have a fail-closed permission entry. Duplicate launch aliases were removed only where they opened the same shared tool; implementations, services, legacy views and data stores remain.

Latest CI for the combined implementation branch: run #273 — **166 passed, 0 failed, 0 skipped**; Restore, Windows x64 publish, Android Agent build/upload and offline tool packaging passed. Artifact: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44786097393/artifacts/14653329474

No physical handset, USB chipset, EDL/BROM protocol, firmware flash or board-level test is claimed. Do not change any feature to Verified without recorded target-environment evidence.


## Final catalog/documentation checkpoint — CI #258

- Catalog: 36 sections, 129 child features, 165 entries; 35 sections In Development, 1 Planned.
- Entry statuses: 24 Planned, 38 In Development, 95 Implemented, 6 Tested, 0 Verified and 2 Unsupported.
- 52 unique executable actions; all have main-window routes.
- CI run #258 on the documentation-updated branch passed all 150 unit tests, Restore, Windows x64 publish, Android Agent build/upload, offline dependency bundling and artifact upload.
- Artifact: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44780672062/artifacts/14652362018
- No physical-device verification is claimed.


## Follow-up implementation — local user and technician security (PR #2)

Section 30 is now In Development rather than Planned. The current implementation has:

- First-run creation of one administrator chosen by the operator; no shared/default password.
- Mandatory authentication before the WPF main window opens.
- Administrator, Technician and ReadOnly roles with a fail-closed map for every one of the 53 catalog ActionIds.
- PBKDF2-HMAC-SHA256 verifiers with random per-account salt; 600,000 iterations by default; no plaintext password storage.
- Minimum 12-character passwords, constant-time verifier comparison and five-failure / 15-minute temporary lockout.
- Administrator creation, role/state changes, password reset for another user, and self-service password change.
- Session lock with re-authentication; if re-authentication is cancelled or fails, the application exits.
- Local bounded audit for sign-in success/failure, account changes, password events, session locks, denied actions and canonical module-action invocation.
- Permission checks in both the module-window buttons and the central action dispatcher. Read-only roles do not receive device-write, workshop-change, account-management, settings, plugin or updater privileges. Interactive screen mirror and automatic Agent installation are blocked for read-only roles; allow-listed read-only command/workflow/batch operations remain available.
- The user-account file lives under the current Windows user's LocalAppData folder. This is application-level authorization and does not replace Windows OS account isolation or a dedicated filesystem ACL/DPAPI policy.

### Explicit remaining gaps

- No custom permission editor; three fixed role templates are used.
- No idle-time lock timer, password recovery workflow or Windows identity/Hello integration.
- Audit events cover authentication, accounts, session events, denied actions and module launch; not every internal WorkshopStore write or the final result of every nested device command.
- Account/audit storage is local to one Windows profile; no central identity or multi-workstation account synchronization.
- No permanent account delete action; accounts are disabled to preserve attribution and history.


## Follow-up — Offline Readiness Center (PR #3)

- Section 28 was extended in place; no extra top-level section was created.
- The Offline Readiness window reads only the current application directory. It checks the seven known bundle items: SoftBox executable, ADB, Fastboot, scrcpy, Android Agent APK, Samsung USB Driver and TRACE Boardviewer.
- Each file is reported as Available, Missing or Unreadable using actual file-system state. Available files display byte size, modified time, version metadata where available and SHA-256 calculated from the actual file bytes.
- Device-profile and board-map folders are inventoried using the files actually present, with a 10,000-file scan cap and reparse-point traversal disabled.
- OS/runtime, storage free space and operational network interfaces are displayed. The view does not claim that an active interface proves Internet connectivity.
- The user can export a local JSON report. The view does not download, install, launch or mutate any inspected component.
- SHA-256 is an integrity fingerprint of the current bytes only; it is not a publisher signature, trusted expected hash or safety certification.
- All **55** executable action IDs have one matching authorization requirement. The Offline Center is a read-only action and is available to ReadOnly accounts.
- Tests validate missing files, exact byte count and SHA-256 for present files, actual data-directory inventory, root-relative paths and real host metadata without Internet claims.


## Additive follow-up — Wireless ADB pairing UI

- Added on branch feature/real-wireless-adb-pairing without creating another top-level section or duplicating the existing pairing service.
- Section 02 Device Center now opens a dedicated Arabic/RTL WPF window for Android Wireless debugging pairing. It uses the existing bundled Google Platform-Tools ADB executable and runs the real adb pair and adb connect operations.
- The pairing endpoint and connection endpoint use separate IP addresses and ports, as displayed by Android. Both host inputs must be IP addresses; the temporary pairing code must be six ASCII digits. The code is entered into a password control, is not persisted, is cleared after the operation and is never included in exported application support reports.
- The operation has bounded ADB command timeouts, cancellation and explicit success/failure handling. A connection attempt is not made if pairing has not been confirmed.
- The new action is routed through the central module action dispatcher and has a fail-closed authorization requirement of DeviceOperations; ReadOnly users cannot invoke it.
- Automated tests cover IPv4/IPv6 endpoint formatting, port validation, code validation, separate pair/connect ports, pair failure, connect failure and cancellation. These tests use an in-memory command runner only in the isolated test project; no simulated device or success result is exposed in the production UI.
- Status: **Tested** for protocol orchestration by automated tests; **not Verified** on a physical handset. Android 11+ and a shared Wi-Fi network are required for the pairing-code workflow.
- No new NuGet package, driver, firmware payload or external executable was added. The feature reuses ADB already bundled by the existing Windows packaging workflow.

## Follow-up — Android Network Diagnostics

- Section 16 now includes the actual Android network interface and route reader in the existing Network Diagnostics workspace.
- The selected authorized ADB serial is passed from Device Center; the service reads `ip address show` and `ip route show` from that exact device, bounds output, supports cancellation and rejects invalid device identifiers before running ADB.
- The Windows host interface/DNS/ICMP tools remain unchanged. The new Android action reports only data returned by Android and does not claim Wi-Fi password access, Internet reachability, signal quality or private network details.
- Unit tests cover selected-device targeting, interface/route output handling, invalid serial rejection, ADB unavailability, empty output and cancellation. No physical phone was connected in CI; the feature is Implemented, not hardware Verified.


## Follow-up — Confirmed third-party Android app removal

- Section 04's existing App Manager now supports removal of the selected third-party package through the existing ADB tools workspace.
- Before sending `adb uninstall`, SoftBox re-reads `pm list packages -3` for the exact selected device and rejects package names that are invalid or are not in that device's third-party package list.
- The UI shows the exact package name and a warning that app data will be removed, and sends no uninstall command unless the operator confirms.
- The result is displayed as successful only when ADB returns its explicit `Success` output. Failed/unknown output is never converted into a success state.
- No system-package selector, arbitrary shell input, private app-data access, new executable or new NuGet dependency was added.
- Automated tests cover the command sequence, rejection of non-third-party/system packages, invalid package names, invalid device serials and ADB failure output. The operation still requires a real authorized Android device for hardware verification.



## Follow-up — Validated Android Split APK installation

- The existing Android App Manager now accepts one Base APK and one or more APK split files (up to 32 total) through a multi-select file picker.
- SoftBox inspects every selected APK locally, including full ZIP entry readability, SHA-256, AndroidManifest parsing, package name, Version Code and the root Manifest split identifier.
- The set is rejected before any device command unless it has exactly one Base manifest (no split identifier), at least one uniquely named split, one matching package name, matching Version Code and consistent Version Name values when supplied.
- The install plan orders Base first, then split files by split identifier. A confirmation shows the target device, package/version and exact files before ADB is invoked.
- Immediately before `adb install-multiple -r`, each local APK is re-hashed; any changed file stops the operation before device access. The result is displayed as success only when ADB returns an explicit `Success` line.
- SoftBox reports signature metadata only; it does not independently verify publisher certificates or APK signer cryptography. Android's package installer enforces signer compatibility and rejects incompatible split sets. No signature bypass is attempted.
- Automated tests cover split-manifest parsing, Base/split ordering, package/version mismatches, missing/duplicate Base or split identities, exact selected-device ADB arguments, post-inspection file modification and failure output. These tests use local APK ZIP fixtures and a fake ADB runner inside the test project only.
- No new NuGet package, external executable, driver or firmware payload was added. The tool reuses the existing APK inspector and ADB command runner.
- The feature is **Tested** for local preflight and command orchestration; it is not hardware **Verified** until tested on an authorized Android device with a real split APK set.

## Additive completion wave — service analytics and workshop documents

- Added printable HTML intake receipts, preliminary estimate documents and service invoices from existing repair-order fields. Customer/device content is HTML-encoded before output.
- Intake receipts state that they record device intake only. Estimates explicitly remain preliminary until a separate customer-approval workflow exists. Service invoices show the saved final amount and recorded parts without marking an order as paid.
- No tax is calculated, no payment is recorded, and the output is not represented as a certified electronic invoice. The existing workshop data schema is unchanged.
- Added live workshop analytics from saved records: open/delivered/cancelled counts, delivered-order recorded revenue, open estimated work value, recorded parts cost, delivery duration where valid timestamps exist, frequent reported-symptom text, serviced models, technician activity, monthly delivered revenue and inventory value at recorded cost.
- Revenue is counted only for delivered orders; open estimates are kept separate. The summary does not claim profit, cash collection, statutory reporting or tax calculations.
- Analytics can be exported to UTF-8 CSV with spreadsheet-formula escaping.
- Automated tests cover analytics aggregation, monthly grouping, CSV formula escaping, document HTML encoding, the estimate disclaimer, invoice payment/tax limitations and recorded parts.
- The validated Split APK implementation from the additive development branch is included: one Base APK plus validated splits are checked locally and installed through adb install-multiple; Android enforces signer compatibility. Automated tests cover set validation, file mutation, selected-device targeting and success output. Hardware verification still requires a real handset.
- Final catalog target after this wave: 36 top-level sections, 130 child features, 166 total catalog entries; 16 Planned, 40 In Development, 96 Implemented, 12 Tested, 0 Verified and 2 Unsupported. These counts describe catalog status, not a completion percentage.


## Additive completion wave — quote decisions and payment ledger

- Workshop schema v5 adds quote decision history and payment records to each repair order. Existing schema v1-v4 JSON records are migrated by initializing the new lists and retaining customers, orders, inventory, movements, receipts and events.
- A technician can record customer approval or rejection together with the quoted amount, contact method, timestamp, note and recording technician. The history is append-only through the store API. It is a manual local record and does not claim a digital signature or independent proof of customer identity.
- Payments can be recorded in multiple installments only after approval of the current quoted amount. Each entry stores amount, entered payment method/reference, timestamp, technician, note and remaining balance. Overpayment is rejected. Price changes that no longer match the accepted quote require renewed approval before another payment can be entered.
- Cancelling an order with recorded payments, rejecting a quote after payment, or reducing the agreed order total below money already recorded is blocked until a dedicated settlement/refund workflow exists.
- Service invoices now display the current quote approval state, recorded payments, approved amount and outstanding balance. A separate printable receipt is available for each selected payment.
- Payment records are manual workshop entries. SoftBox does not process payments, call a bank/payment provider, verify settlement or issue refunds. Tax computation and certified e-invoicing remain unsupported.
- Analytics distinguish delivered-order recorded revenue, manually entered payments and outstanding approved quote balances. These are operational records, not profit or external cash reconciliation.
- Unit/integration tests cover schema v4-to-v5 migration without data loss, approval gating, partial/full payments, overpayment prevention, price revision/re-approval, cancellation safeguards, persistence, CSV analytics and HTML-safe payment receipts.


## Additive follow-up — encrypted local data and Dashboard Analytics

- Added a Windows DPAPI CurrentUser data-protection service with a versioned file marker and distinct purpose-derived entropy. The account/audit store, workshop data and activity/notification history use separate protection purposes.
- The workshop workshop.json and activity activity.json paths are preserved for compatibility, but their stored bytes are now DPAPI-protected. Existing plaintext workshop schemas v1-v5 and activity schema v1 are read, validated, and atomically rewritten in protected form on first successful access. If an encrypted file cannot be decrypted, loading fails visibly; SoftBox never silently resets it.
- Manual workshop backups now default to the encrypted .sbxbackup format. The restore picker continues to accept legacy .json exports. DPAPI backups are tied to the same Windows user context and are not portable to another Windows account or machine; exported reports remain separate plaintext files.
- Added Dashboard Analytics as a real read-only window routed from the Dashboard section. It computes metrics from persisted activity/notifications: unique device fingerprints, history totals, 24-hour/7-day/30-day activity, unread notification counts, frequent event/category summaries and a 14-day UTC trend. It explicitly does not claim a background job queue.
- Dashboard Analytics has a canonical action ID, ViewWorkspace authorization, central MainWindow routing, and automated aggregation tests. DPAPI purpose separation, protection, plaintext migration, encrypted workshop backup/restore and encrypted activity-store migration are covered by tests.
- DPAPI uses the Windows account protection context; loss of the Windows profile/DPAPI master-key recovery capability can prevent restoration. This is not a cloud key-management or portable backup solution.
- Final catalog totals for this branch: 36 sections, 130 child features, 166 total entries; 14 Planned, 40 In Development, 96 Implemented, 14 Tested, 0 Verified and 2 Unsupported. These labels describe catalog coverage, not product completion percentage. No hardware feature is marked Verified.


## Additive follow-up — Samsung service modes, Odin hand-off and Logcat triage

- Added Samsung Download Mode entry through the shared Android service workspace. Before sending `adb reboot download`, SoftBox revalidates the selected serial and confirms Samsung manufacturer/brand properties.
- Added a separate Samsung Tools Odin Assistant for BL/AP/CP/CSC/HOME_CSC selection, slot consistency, TAR/TAR.MD5 extension checks, SHA-256 inspection and re-hashing before launching a user-provided Odin executable.
- Odin is not bundled, and SoftBox does not automate undocumented Odin controls, inject package files, press Start or report a successful flash.
- Fastboot Manager now offers selected-device `fastboot reboot recovery` and `fastboot reboot fastboot` transitions with confirmation and serial validation.
- Added a bounded local Logcat analyzer for recent crash, ANR, memory, permission, SELinux, thermal, storage, USB and network indicators. It exports grouped JSON summaries rather than raw log lines.
- Windows CI run #351 passed **332 tests**, Windows x64 publish, Android Agent build, offline tool bundling and artifact upload. This remains CI verification; it does not replace physical handset or vendor-protocol verification.

## Additive follow-up — DPAPI-protected Android backup scheduling

- Added `AndroidBackupScheduleStore`, which validates schedule definitions, protects stored schedule/password JSON using Windows DPAPI CurrentUser and migrates a legacy plaintext file to protected bytes on successful read.
- Added on-connect, daily and weekly local-time scheduling, duplicate suppression, per-schedule execution state and a background encrypted SBX backup runner.
- Integrated the scheduler into the authenticated desktop session with independent 30-second device polling, including when visible auto-discovery is disabled. ReadOnly accounts do not execute scheduled device backups.
- Added a dedicated Smart Backup workspace to create, list, pause/resume, delete and run schedules immediately. The UI refreshes when a job finishes.
- Added policy, DPAPI store and service orchestration tests. The scheduler requires SoftBox to remain open and the Windows user to remain signed in; it does not create a Windows Task Scheduler job or wake a sleeping PC.


## Current main audit and additive gap closure — 21 November 2026

This checkpoint supersedes older catalog totals above. Historical sections are retained as implementation history; the current counts below are derived from the current catalog and tests.

- **36/36 sections** remain in the specified order. Every section has an audited feature entry; no empty top-level section is hidden behind a placeholder.
- **139 child features / 175 total catalog entries**, counting the 36 section rows.
- Child feature status: **101 Implemented, 23 Tested, 4 In Development, 9 Planned, 2 Unsupported, 0 Verified**.
- Planned and Unsupported entries have no executable Run action. Existing shared tools retain one canonical action and implementation; the additions in this wave add feature metadata under the existing section action rather than creating duplicate launch routes.
- Section 16 now includes a real phone-origin network check: DNS property observations plus one bounded ICMP request from the selected Android device.
- Section 22 now includes text search, operation-type filtering and filtered CSV export for per-device repair history. The export includes masked serial only; it excludes the device fingerprint and raw serial.
- Existing Fastboot inspection, Android app lifecycle, runtime permissions, device service transitions, APK cryptographic verification, resumable Firmware download, and Workshop quote/payment documents are now labelled **Tested** because the corresponding automated test files cover their core operations. This is test-suite coverage, not physical handset, vendor hardware or real firmware verification.
- No physical-device feature is marked Verified.

### Remaining non-executable or incomplete capabilities

| Section | Feature | Current state and reason no executable action is exposed |
|---|---|---|
| 03 Device Intelligence | Complete per-model capability matrix | In Development. The conservative engine exists and blocks unknown or unsupported operations; reviewed coverage across more models and evidence sources remains incomplete. |
| 06 Samsung Tools | Low-level Download Mode/Odin automation | Planned. The Odin hand-off assistant exists, but undocumented UI automation or low-level flashing is not implemented. |
| 07 Xiaomi Tools | Xiaomi-specific Fastboot service workflows | Planned. Generic Fastboot inspection exists; no model/region-verified Xiaomi service sequence is present. |
| 08 Android Brands | Brand-specific service workflows | Planned. Generic Android properties exist; dedicated Pixel, Motorola, OPPO, vivo, realme, Huawei, TECNO and Infinix protocols are not verified. |
| 09 Qualcomm Tools | Qualcomm EDL recovery | Planned. No signed, model-matched programmer or verified secure-boot/authorization workflow is available. |
| 10 MediaTek Tools | BROM/Preloader recovery | Planned. No signed, model-matched Download Agent or verified authorization flow is available. |
| 11 Firmware Center | Firmware-to-device compatibility gate | Planned. Archive hash/metadata inspection do not establish publisher trust, exact board/region compatibility or anti-rollback safety. |
| 14 Phone Link Studio | Multi-session real-device reliability | In Development. Session UI and concurrency policy exist; codec, lifecycle and multi-device behavior still require target-device testing. |
| 15 Hardware Diagnostics | Board-level electrical tests | Unsupported. These require model-specific schematics, a validated fixture and safe measurement probes; the existing external measurement-rig protocol does not claim to provide these board-level tests. |
| 17 Smart Repair AI | Expanded model-specific offline repair knowledge | In Development. The existing advisor is deterministic and evidence-linked; a reviewed model-specific repair knowledge base is not complete. |
| 19 Safe Recovery | Official model-specific recovery procedures | Unsupported. Current preflight is inspection-only and blocks execution; there is no verified, signed, model-matched recovery sequence with proven compatibility and anti-rollback safety. |
| 26 Plugin Store | Publisher signature trust roots | Planned. ZIP/manifest inspection exists, but trusted publisher root management is not implemented. |
| 26 Plugin Store | Plugin installation, dependency lifecycle and sandbox runtime | Planned. Deliberately disabled until permission grants, dependency lifecycle and process isolation are implemented and tested. |
| 31 Update Center | Signed self-updater | Planned. Release metadata inspection exists, but there is no trusted application-signing root, staged installer or verified rollback mechanism. |
| 31 Update Center | Release rollback policy | In Development. Stable/Beta metadata checks exist; installing a prior version and recovering safely are not implemented. |

### Hardware and safety boundary

Qualcomm EDL, MediaTek BROM/Preloader, undocumented Samsung Download/Odin control, OEM unlock/erase/flash, FRP or account-lock bypass, and private application-data extraction are not represented as working tools. They remain unavailable unless a lawful, vendor-documented, model-specific protocol with explicit authorization and reliable verification can be established. A successful Windows CI run or unit-test suite does not constitute physical-device verification.


## PR #21 additive implementation wave — current branch audit

This section supersedes the older additive snapshot above and is derived from the current catalog plus automated test files.

- **36 master sections, 146 child features, 182 total catalog entries.**
- Child feature status: **100 Implemented, 35 Tested, 3 In Development, 6 Planned, 2 Unsupported, 0 Verified**.
- Every section has at least one real catalog feature. Existing actions keep one canonical ActionId; new subfeatures that share an existing window are metadata-only, not cloned navigation routes.
- **Samsung:** the Odin assistant now reads TAR entry names without extraction, verifies an appended TAR.MD5 checksum and records partition hints/SHA-256. This still does not authenticate the firmware publisher or establish model/CSC/bootloader compatibility, and it never automates Odin Start.
- **Firmware:** the compatibility gate checks available manufacturer/model/product/board/region/Anti-Rollback and local integrity facts. Unknown evidence blocks progression; publisher signature remains Unknown until an independent trusted publisher key and verified source signature exist.
- **Xiaomi Fastboot:** the Xiaomi window can confirm ADB device state and brand, request a user-confirmed reboot to Bootloader, wait for the exact same serial in Fastboot and report getvar/lock/Verified Boot/Anti-Rollback values. No unlock, flash, erase or relock operation is exposed.
- **Qualcomm/MediaTek:** Windows PnP transport readiness reports driver status and missing authorization/programmer/DA prerequisites. No EDL/BROM transport session or low-level write operation exists.
- **Measurements:** real serial-fixture readings can be compared with technician-supplied, source-cited JSON limits. Missing values remain NotMeasured; no factory tolerances are invented.
- **Repair knowledge:** DPAPI-protected local technician records support add, edit, delete and search, with source, evidence, author and explicit safe steps. Repair Advisor opens the knowledge store prefiltered using the selected device model; it contains no preloaded OEM repair database.
- **Vendor diagnostics:** fixed, read-only property profiles cover major Android vendors; they do not imply proprietary download, flash or recovery protocols.
- **Plugins:** RSA-PSS signed ZIP packages can be checked against public keys that the user imports after independently confirming their fingerprint. Signed JSON workflows execute only fixed read-only Android presets after a per-session permission confirmation and fresh ADB authorization. Native DLL loading/execution remains planned and disabled.
- **Updates:** signed Windows update assets are validated and staged; install/rollback helper scripts have path/hash/reparse-point safeguards and preserve the prior install snapshot. Automated tests validate staging and generated helper logic; no physical production updater run is claimed. A trusted public key must be imported out-of-band, and the repository does not contain a private release-signing key.
- No physical device feature is marked Verified. A green CI run is not proof of EDL/BROM, Odin flashing, a board measurement fixture or an actual production update installation.

### Remaining blocked or incomplete work

| Section | Item | State | Reason |
|---|---|---|---|
| 03 | Full model-by-model capability coverage | In Development | Current identity/capability engine is fail-closed, but the curated evidence base needs more reviewed models and board revisions |
| 06 | Automated Odin/Download Mode flashing | Planned | No supported documented Odin API/transport is available here; the current workflow is a real package inspector and manual external-tool hand-off |
| 07 | Xiaomi-specific Fastboot flash/unlock service sequences | Planned | Read-only Fastboot inspection is implemented; per-model OEM authorization and a verified service protocol for Unlock/Flash/Relock remain unavailable |
| 08 | Dedicated service protocols for other vendors | Planned | Vendor property reads exist; private/undocumented protocols are not fabricated |
| 09 | Qualcomm EDL programmer and recovery execution | Planned | Requires the correct signed model-specific programmer, OEM authorization and secure-boot evidence |
| 10 | MediaTek BROM/Preloader DA recovery | Planned | Requires a signed matching Download Agent and OEM authorization |
| 14 | Physical multi-device session reliability | In Development | Up to four scrcpy processes/windows are managed and monitored; handset/codec/hotplug validation remains to be performed on target devices |
| 15 | Direct board-level component measurements | Unsupported | A real compatible fixture and model-specific schematics/probes are required; only externally measured values plus technician-provided reference thresholds are supported |
| 17 | Curated model-specific OEM repair dataset / AI model | In Development | Encrypted technician knowledge CRUD and evidence-based deterministic advisor exist; there is no embedded licensed OEM database or trained AI model |
| 19 | Official model-specific recovery/flash execution | Unsupported | Compatibility preflight is informative and fail-closed; no verified signed recovery package/protocol is available |
| 26/27 | Native DLL plugin runtime and process isolation | Planned | Only signed data-only read-only workflow plugins are supported; arbitrary code execution remains disabled pending hardened Windows isolation/IPC and security testing |
| 31 | Production signed release adoption | In Development | Pipeline code and helper logic are tested, but the public release-signing key must be confirmed independently and a signed production asset must be published/tested |



## Additive completion follow-up — 21 November 2026

- **36 master sections / 151 child features / 187 total catalogue entries** after this follow-up.
- Child-feature states: **99 Implemented, 41 Tested, 3 In Development, 6 Planned, 2 Unsupported, 0 Verified**. A Tested software policy is not the same as physical handset validation.
- Device Intelligence exports a coverage matrix from the loaded profile catalogue: model numbers, device codes, Product IDs, SoC aliases, Board IDs, BoardMapId references and missing evidence. Missing board information stays missing; a reference alone does not prove an actual verified PCB map.
- Multi-screen start repeats `adb -s <serial> get-state` after scrcpy setup and immediately before launching the process. The manager continues checking device authorization, process lifetime and embedded-window health. Physical multi-phone, codec and hotplug validation remains outstanding.
- Xiaomi Tools inspects `.tgz`, `.tar.gz` and `.tar` Fastboot ROMs without extraction or script execution, reading `android-info.txt`, image names, flash partitions and erase/unlock commands, and matching package Product against the last value read from the selected Fastboot device. It does not flash or unlock.
- Generic vendor property-profile coverage is classified as Tested for automated policy/parser tests only. No OEM proprietary service protocol is implied.
- Update Center Stable/Beta sorting, signed-package generation and rollback helpers have automated test coverage. The release signer builds a manifest from the published binary files and signs the archive with RSA-PSS; tag publishing stops when the private-key secret is absent.
- Production release signing is **not yet externally validated**: a maintainer must configure `SOFTBOX_RELEASE_PRIVATE_KEY_PEM`, independently publish the matching public-key fingerprint and import the public key in Update Center. The private key is not committed.

### Remaining vendor / physical-device boundaries

- Samsung Odin Start automation and low-level Download Mode operations remain unavailable; the existing tools validate TAR/TAR.MD5 packages and hand off to the official Odin executable supplied by the operator.
- Xiaomi unlock/flash/relock remains unavailable; ROM inspection is read-only and product mismatch is reported.
- Qualcomm EDL programming still requires an OEM-authorized signed model-matched Firehose programmer; current tools detect Windows PnP readiness only.
- MediaTek BROM/Preloader programming still requires an OEM-authorized signed Download Agent; current tools do not start a BROM programming session.
- Native DLL plugins remain disabled pending a hardened isolated process host and security review.
- Board-level electrical probing requires real model-specific schematics, fixture and probes; the generic external measurement reader is not a board-level test instrument.
- Official model-specific recovery/flash execution stays blocked until package trust, exact board/region/rollback evidence and an authorized protocol are available.
