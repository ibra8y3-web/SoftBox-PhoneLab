# SoftBox Master Module Audit and Development Roadmap

Audit baseline: pre-implementation `main` snapshot, commit `12650bed442498f7746432de42bea95b1a622381`, 20 November 2026. APK Lab changes are recorded on `feature/apk-lab-real-apk-inspector` until CI review is complete.

This document maps the supplied **SoftBox – Universal Smart Device Service & Repair Platform** specification to the code that already exists. It is an implementation inventory, not a claim that every module is complete.

### Audited catalog snapshot

- Top-level sections: 36 total — 22 In Development and 14 Planned.
- Catalog entries: 151 total (36 sections plus 115 child features): 58 Implemented, 4 Tested, 64 Planned, 23 In Development and 2 Unsupported.
- No item is marked Verified in this snapshot. A successful Windows CI run is not physical-device verification.

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
| 01 | Dashboard | In Development | Live ADB availability, connected-device list and immediate status text | Central activity counters, job queue, history, persistent notifications, storage and update health |
| 02 | Device Center | In Development | ADB auto-discovery every 3 seconds, multiple-device listing and selection | Wireless pairing UI, sessions, group targeting, per-device job isolation and batch controls |
| 03 | Device Intelligence | In Development | Android property parsing, model-profile matching, community model-code catalog, verified board-map import rules | Per-model capability matrix, richer provenance, protocol/driver compatibility and reviewed device coverage |
| 04 | Android Service Center | In Development | Authorized ADB inspection, local Agent bridge, Android evidence capture | ADB GUI manager, app/package manager, Android file manager and safe command catalog |
| 05 | APK Lab | In Development | Local APK Lab window; SHA-256; readability of every non-directory ZIP entry; text XML and Android Binary XML manifest parsing; permission/version extraction; structural signature metadata detection | Cryptographic signature/certificate validation, resource resolution, two-version diff and DEX/resource analysis; no installation or device action |
| 06 | Samsung Tools | In Development | Samsung device identification through the shared device engine; official Samsung USB driver is surfaced under USB Doctor | Samsung Download Mode visibility, model-specific service functions and official compatible recovery workflows |
| 07 | Xiaomi Tools | Planned | Generic Android/ADB path only | Xiaomi/Redmi/POCO identity, region compatibility and supported model-specific workflows |
| 08 | Android Brands | In Development | Generic manufacturer/model identification and community identity profiles | Dedicated, evidenced modules for Pixel, Motorola, OnePlus, OPPO, realme, vivo, Huawei, Honor, TECNO, Infinix and others |
| 09 | Qualcomm Tools | Planned | SoC identification hints may be read from Android | Qualcomm USB/EDL detection, signed compatible recovery sources and a verified model support matrix |
| 10 | MediaTek Tools | Planned | SoC identification hints may be read from Android | Preloader/BROM discovery, driver diagnostics and verified model-specific official recovery |
| 11 | Firmware Center | In Development | Local archive inspection, SHA-256 and common Android OTA metadata/payload detection | Trusted source catalog, download manager, signature verification, region/model compatibility and firmware library |
| 12 | USB Doctor | In Development | ADB/Fastboot inventory, transport explanations and optional official Samsung driver | Windows Device Manager inventory, VID/PID, driver health/conflict/signature diagnosis and broader OEM driver catalog |
| 13 | Smart Backup | Planned | Workshop JSON database backup/restore only; its shortcut opens the existing workshop backup tab | Phone-file backup, integrity manifests, encryption/key management, scheduling and restore verification |
| 14 | Phone Link Studio | In Development | Authorized Android screen mirror/control using scrcpy | Integrated recording, multiple independent sessions, clipboard/audio controls and quality profiles |
| 15 | Hardware Diagnostics | In Development | Agent screen/touch/audio/microphone/vibration/sensor checks, Camera2 routes, screen-photo analysis and external meter readings | More model-tested tests, durable per-session evidence, calibrated lab procedures and supported board-level fixtures |
| 16 | Network Diagnostics | In Development | Local Windows interface inventory, DNS resolution and one user-selected ICMP probe with bounded timeouts/cancellation | Permitted Android network observations, Wi-Fi details and ADB Wireless pairing; no phone-private network claim |
| 17 | Smart Repair AI | Planned | External iFixit repair-guide search only (not AI) | Evidence-linked reasoning, confidence-ranked hypotheses and a locally searchable repair knowledge base |
| 18 | One-Click Diagnostic | In Development | One action reads available system properties and rebuilds component evidence | A consent-aware orchestrator for supported non-destructive tests, cancellation and one combined report |
| 19 | Safe Recovery | Planned | Read-only firmware pre-inspection exists in Firmware Center | A central compatibility/consent/backup gate and only officially supported recovery integrations; no flashing is currently implemented |
| 20 | Workflow Automation | Planned | No workflow designer or runner | Saved workflows, conditional steps, cancellation, safe retry policy and detailed step history |
| 21 | Batch Operations | Planned | Multiple devices can be discovered and displayed | Explicit target selection, concurrent limits, per-device isolation/results and independent cancellation |
| 22 | Repair History | In Development | Workshop order records, timeline notes and capped local activity events | Unified per-device diagnostic/session history, searchable reports and controlled retention |
| 23 | Workshop Manager | In Development | Customers, repair orders, status tracking, QR ticket, estimates/final cost, workshop settings and reports | Intake accessory checklist, customer approval flow, photos with consent, receipts, quotes, invoices and multi-user workflow |
| 24 | Parts Inventory | In Development | SKU items, quantities, thresholds, stock adjustments and event history; its shortcut opens the existing inventory tab | Suppliers, purchase orders, part consumption on repair orders, cost/sale accounting and analytics |
| 25 | Service Reports | In Development | Device JSON reports; workshop text/HTML and UTF-8 CSV exports | Native PDF/Excel outputs, unified analytics, invoice templates and signed report provenance |
| 26 | Plugin Store | Planned | No plugin installation/catalog runtime | Trusted catalog, signature verification, dependencies, permissions, updates and uninstall lifecycle |
| 27 | Plugin SDK | Planned | No public contracts or SDK package | Versioned API, manifest, examples, templates, permission declarations and developer documentation |
| 28 | Offline Center | In Development | Local ADB, inspection, workshop records and offline bundled utilities | Central online/offline status, local package management and offline knowledge/update policy |
| 29 | Security & Privacy | In Development | ADB argument-list invocation, serial validation, bounded timeouts, Agent consent and read-only forensic flows | Encrypted local data, backup key management, centralized audit, privacy controls and reviewed threat model |
| 30 | User & Technician Management | Planned | Technician name can be written on a repair order; this is not user authentication | User accounts, secure password handling, roles, section/action permissions, session lock and per-user audit |
| 31 | Update Center | Planned | Device model catalog refresh only | Signed application updates, release channels, integrity checks, rollback policy and update history |
| 32 | Notification Center | Planned | Current-operation status text only | Persistent notifications, unread state, filtering and navigation to related modules |
| 33 | Help Center | In Development | USB/ADB and Agent instructions plus repository documentation | Searchable in-app help, glossary, FAQs and bundled offline knowledge |
| 34 | Support & Logs | In Development | Bounded Android log/evidence export and firmware inspection messages | Structured SoftBox logs, crash reports, secret redaction and an explicit-consent support bundle |
| 35 | Settings | In Development | Global Workshop locale, currency, time-zone, business profile and warranty settings | App-wide language/theme/shortcuts, device preferences, privacy and storage settings |
| 36 | About | Planned | Version, project and third-party notice information exists in source/docs | Dedicated About page with current version, licenses, runtime/tools and links |

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
