# SoftBox Master Module Audit and Development Roadmap

Audit baseline: pre-implementation `main` snapshot, commit `12650bed442498f7746432de42bea95b1a622381`, 20 November 2026. APK Lab changes are recorded on `feature/apk-lab-real-apk-inspector` until CI review is complete.

This document maps the supplied **SoftBox – Universal Smart Device Service & Repair Platform** specification to the code that already exists. It is an implementation inventory, not a claim that every module is complete.

### Pre-implementation main snapshot

- Top-level sections: 36 total — 22 In Development and 14 Planned.
- Catalog entries: 151 total (36 sections plus 115 child features): 58 Implemented, 5 Tested, 63 Planned, 23 In Development and 2 Unsupported.
- No item is marked Verified in this snapshot. A successful Windows CI run is not physical-device verification.

### Current additive branch snapshot — Offline Readiness Center follow-up

- Top-level sections: **36 retained** — all 36 In Development.
- Child features: **129**; catalog entries including section rows: **165**.
- Entry statuses (section rows plus child features): **18 Planned, 39 In Development, 96 Implemented, 10 Tested, 0 Verified and 2 Unsupported**.
- Executable navigation: **55 unique ActionIds**, all routed, with no duplicate executable action IDs.
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
| 01 | Dashboard | In Development | Live ADB availability, connected-device selection, persisted device-connect/disconnect and inspection notifications, current status | Job counters/queue, historical metrics, storage health and update health aggregation |
| 02 | Device Center | In Development | ADB discovery, multiple-device listing and selection; serial-scoped operations; local device connect/disconnect/authorization history; real Wireless debugging pairing via adb pair/connect with separate IP/port endpoints and one-time code; protocol tests passed | Persistent device sessions, group targeting and independent background job queue; no physical-phone wireless pairing verification yet |
| 03 | Device Intelligence | In Development | Android property parsing, model-profile matching, community model-code catalog, verified board-map import rules | Per-model capability matrix, richer provenance, protocol/driver compatibility and reviewed device coverage |
| 04 | Android Service Center | In Development | Authorized ADB inspection, local Agent bridge, public shared-storage browser/transfers, installed third-party package inspection, APK install after manifest inspection and confirmation, allow-listed read-only commands; Fastboot Manager discovers fastboot devices, reads getvar all and supports confirmed reboot to Android | Full package uninstall/split APK workflows, free-form shell commands, private app-data operations and model-specific destructive Fastboot servicing remain unsupported |
| 05 | APK Lab | In Development | Local APK Lab window; SHA-256; readability of every non-directory ZIP entry; text XML and Android Binary XML manifest parsing; permission/version extraction; structural signature metadata detection | Cryptographic signature/certificate validation, resource resolution and DEX/resource analysis; no installation or device action |
| 06 | Samsung Tools | In Development | Official Samsung USB driver entry under USB Doctor; fixed-key getprop collection for One UI, CSC, PDA, bootloader and Verified Boot with JSON report | Download Mode service protocol, model-specific recovery and officially verified firmware workflow |
| 07 | Xiaomi Tools | In Development | Xiaomi/Redmi/POCO fixed-key getprop collection for model/build/MIUI-HyperOS/region/Verified Boot with JSON report | Model/region compatibility database and official model-specific Fastboot/recovery workflows |
| 08 | Android Brands | In Development | Generic read-only Android manufacturer/brand/model/build and Verified Boot property collection | Dedicated, evidenced service protocols for Pixel, Motorola, OnePlus, OPPO, realme, vivo, Huawei, Honor, TECNO, Infinix and others |
| 09 | Qualcomm Tools | In Development | SoC identification hints plus an automatically opened Windows PnP view filtered to the common 05C6:9008 EDL USB identity hint | EDL protocol, programmer verification, secure-boot compatibility and signed model-specific recovery (not implemented) |
| 10 | MediaTek Tools | In Development | SoC hints plus an automatically opened Windows PnP view filtered to the common 0E8D:0003 Preloader/BROM USB identity hint | BROM protocol, Download Agent verification, secure-boot compatibility and signed model-specific recovery (not implemented) |
| 11 | Firmware Center | In Development | Local firmware import/catalogue by vendor/model/region/version, file SHA-256, ZIP/OTA archive inspection and post-import integrity revalidation | Trusted source catalog, signed publisher verification, download manager and device/model/region/rollback compatibility gate |
| 12 | USB Doctor | In Development | ADB/Fastboot transport inspection; Windows SetupAPI PnP inventory with VID/PID, friendly name, service and problem code; conservative Samsung/Google/Qualcomm/MediaTek mode hints; official Samsung driver entry | Driver package signature verification, controlled install/remove/rollback and OEM driver catalogue; no driver mutation in the current PnP inspector |
| 13 | Smart Backup | In Development | ADB shared-storage backup into ZIP with per-file SHA-256; optional `.sbx` AES-256-CBC + HMAC-SHA256 using PBKDF2-HMAC-SHA256 (600,000 iterations); authenticated verification; verified ZIP/SBX restore to a unique new shared-storage directory without overwrite/delete | Scheduled backups, key recovery policy, automatic reconnect jobs and app-private/system data backup (unsupported by design) |
| 14 | Phone Link Studio | In Development | Verified scrcpy screen mirroring/control; real MP4/MKV screen recording is wired to the selected authorized ADB device, requires user-chosen new output path and finalizes via WM_CLOSE before validating non-empty output file | Hardware recording validation across Android versions/codecs, multi-session concurrent display and app-side recording library management |
| 15 | Hardware Diagnostics | In Development | Agent screen/touch/audio/microphone/vibration/sensor checks, Camera2 routes, screen-photo analysis and external meter readings | More model-tested tests, durable per-session evidence, calibrated lab procedures and supported board-level fixtures |
| 16 | Network Diagnostics | In Development | Local Windows interface inventory, DNS resolution and one user-selected ICMP probe; authorized Android interface-address and IP-route observations through ADB with bounded timeouts/cancellation | Wi-Fi radio/security metadata, internet reachability from the phone and model-specific connectivity diagnostics remain unimplemented; no phone-private or unavailable data claim |
| 17 | Smart Repair AI | In Development | Deterministic local evidence-linked advisor over battery/storage readings, Agent results and component diagnostics with severity, confidence, evidence and safe next steps; external repair guide search | LLM-backed online mode and expanded model-specific offline repair knowledge; current local advisor is rules-based, not an AI model |
| 18 | One-Click Diagnostic | In Development | One action runs five read-only ADB presets (properties, battery, storage, uptime, third-party apps), supports cancellation and saves a device-masked JSON report | Coordinated opt-in hardware test suite and consolidated lab instrumentation/Agent evidence |
| 19 | Safe Recovery | In Development | Conservative preflight is wired into Firmware Center; re-hashes selected file, compares exact manufacturer/model against the current authorized device snapshot, and reports Passed/Failed/Unknown/Blocked with evidence; automated tests enforce never-allow while publisher trust, signed compatibility rules, rollback and verified-backup proof are unavailable | Official publisher trust roots, signed model/region/board compatibility database, anti-rollback policy evidence and vendor-authorized recovery workflow; no flashing is enabled |
| 20 | Workflow Automation | In Development | JSON workflow builder/save/load and sequential read-only ADB runner with allow-list validation, cancellation and step-level results | Conditions, triggers, safe retry policies and broader workflow actions beyond read-only presets |
| 21 | Batch Operations | In Development | Multi-device selection and bounded concurrent read-only ADB preset execution with per-device results and configured concurrency limit | Additional allowed batch actions, independent per-device cancellation and durable batch job queue |
| 22 | Repair History | In Development | Persistent local per-device connection/authorization/inspection/Agent/report history using SHA-256 device fingerprint and masked serial; existing workshop event/timeline history retained | Broader linkage of every service action and diagnostic result, searchable retention/cleanup and encrypted local store |
| 23 | Workshop Manager | In Development | Customers, repair orders, status tracking, QR ticket, estimates/final cost, workshop settings and reports | Intake accessory checklist, customer approval flow, photos with consent, receipts, quotes, invoices and multi-user workflow |
| 24 | Parts Inventory | In Development | SKU catalogue with category/supplier/cost/sale/reorder fields in the dedicated Parts Inventory window; supplier stock receipts record supplier and invoice, quantity and purchase unit cost, update weighted-average cost, and atomically add a purchase receipt plus stock movement; order-linked consumption deducts stock and records parts cost; inventory/movement/purchase CSV exports | Multi-line purchase orders, invoice approval/payment tracking, barcode scanning and multi-branch synchronization |
| 25 | Service Reports | In Development | Device JSON diagnostics; repair-order HTML/text/UTF-8 CSV includes parts and parts cost; inventory and movement CSV exports | Native PDF/Excel, signed report provenance and aggregated analytics dashboards |
| 26 | Plugin Store | In Development | Local ZIP/Manifest inspection, permission allow-list validation, Zip Slip/symlink/duplicate-path/size/ratio checks and package SHA-256; install and code execution remain disabled | Publisher signature/trusted-root verification, permission grant UI, dependency manager, install/update/uninstall lifecycle and sandboxed runtime |
| 27 | Plugin SDK | In Development | Version-1 manifest contract, semantic version validation, known permission identifiers, fail-closed unknown permissions, sample manifest and developer quickstart | Public SDK assembly/contracts, actual host API, runtime IPC, sample executable plugin and compatibility test host |
| 28 | Offline Center | In Development | Read-only local package inventory for the SoftBox EXE, ADB, Fastboot, scrcpy, Android Agent APK, Samsung USB Driver and TRACE Boardviewer; actual presence/missing/unreadable state, file size, modified time, executable version metadata and SHA-256; actual file counts and aggregate sizes in device-profile and board-map directories; OS/runtime/network-interface/storage inventory; JSON export; link/Junction paths rejected | Internet reachability validation, expected-source hash/signature verification, package repair/download/install and broader local component discovery remain unimplemented |
| 29 | Security & Privacy | In Development | ADB argument-list invocation and serial validation; allow-listed read-only shell presets; Agent consent; safe public-storage paths; secret-redacted local support ZIP; masked device history identifiers | Encrypted local workshop/history storage, managed keys, signed updater/plugin trust roots and formal threat model |
| 30 | User & Technician Management | In Development | First-run administrator bootstrap; mandatory local sign-in; administrator/technician/read-only roles; PBKDF2-HMAC-SHA256 password verifiers; password lockout; account administration; self-service password change; manual session lock/re-authentication; bounded audit for sign-in, account, session, denied-action and module-action events | Windows OS account binding and dedicated ACL/DPAPI protection; unattended idle timeout; custom per-user permission editor; password recovery policy; full attribution of every internal WorkshopStore mutation and all nested device-operation outcomes; central multi-workstation identity |
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
