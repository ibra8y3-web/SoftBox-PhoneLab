# SoftBox Professional Service Platform Expansion

Date: 24 November 2026  
Scope: additive expansion of the existing SoftBox PhoneLab product architecture from 38 to 51 top-level sections.

## Product direction

SoftBox is being expanded into a multi-brand, multi-chipset smartphone service workstation. The intended breadth is comparable to large professional servicing suites: separate brand workspaces, chipset engines, firmware engineering, boot/recovery, storage and board-level labs, diagnostics, evidence, and workshop automation.

Commercial service catalogues demonstrate the breadth of this market. EFT Pro publicly groups hundreds of operations across General, MediaTek, Qualcomm, Rockchip, Spreadtrum, Allwinner, Apple, Samsung, Huawei and Xiaomi. UMT separates modules by chipset families and publishes a searchable model catalogue. Z3X describes distinct products for Samsung, MediaTek-based Pandora and eMMC/JTAG storage work. Samsung's official Smart Switch recovery guide documents an owner-facing emergency software recovery process. These references are used only to benchmark product organization, not to copy proprietary protocols or claim identical support.

References:
- EFT Pro feature catalogue: https://www.eftprodongle.com/features
- UMT supported modules: https://www.ultimatemultitool.com/modules-support/
- UMT supported models: https://www.ultimatemultitool.com/supported-models/
- Z3X product families: https://z3x-team.com/products/
- Samsung official emergency software recovery: https://update.kies.samsung.com/SmartSwitchPC_recovery_guide/firmware/emergencyrecovery/

## What was added to the product architecture

The master catalogue now contains 51 ordered sections. The original 38 sections and their canonical tools remain in place. Thirteen specialist sections have been added to navigation and the module catalogue with Planned status and no ActionId. They are roadmap destinations, not simulated or working tools.

| Section | Specialist workspace | Planned service families |
|---:|---|---|
| 39 | Qualcomm EDL & Diag Lab | EDL/9008 evidence, Firehose package validation, rawprogram/patch parsing, verified readback, restore preflight |
| 40 | MediaTek BROM/Preloader/META Lab | USB mode identification, Scatter parsing, DA/Auth validation, META diagnostics, backup/restore planning |
| 41 | Unisoc/Spreadtrum Lab | PAC inspection, USB/DIAG evidence, partition maps, device information, recovery preflight |
| 42 | Rockchip/Allwinner Lab | MaskROM/Loader discovery, firmware parsing, partition/image maps, readback verification, recovery planning |
| 43 | Huawei/Honor Kirin Lab | COM/Fastboot/Upgrade modes, APP/dload inspection, board/region matching, official recovery handoff, boot diagnostics |
| 44 | Firmware Engineering Studio | Multi-format readers, official source catalogue, signature trust, firmware diff, compatibility ledger |
| 45 | Partition & Filesystem Lab | GPT/MBR, Android Sparse, Dynamic/Super partitions, read-only ext4/F2FS inspection, map comparison |
| 46 | eMMC & UFS Memory Lab | Adapter inventory, CID/CSD/EXT_CSD/UFS identification, health data, image integrity, strict write preflight |
| 47 | Boot/Recovery/Rescue Center | Unified transport matrix, bootloop diagnostics, OTA/sideload guidance, recovery monitoring, before/after evidence |
| 48 | Boardview & Component Repair Lab | Source-backed board maps, component cross-reference, test-point references, instrument readings, repair history |
| 49 | Network/Modem/RF Lab | Baseband/SIM status, Wi-Fi/Bluetooth/GPS/NFC tests, carrier status, redacted modem logs, external RF meters |
| 50 | Data Rescue & Migration Lab | Owner-authorized backup, selective migration, backup verification, encrypted vault, access-boundary report |
| 51 | Service Operations & Automation Center | Durable job queue, per-device isolation, model-aware workflow templates, multi-device orchestration, evidence bundles |

## Important existing tools that remain canonical

The new sections will extend rather than clone existing services:
- Samsung Odin remains in Samsung Tools.
- Xiaomi Fastboot diagnostics remain in Xiaomi Tools.
- Existing Qualcomm/MediaTek USB diagnostics remain in their current sections until specialist protocol engines are independently implemented.
- Existing Firmware Center remains the canonical firmware import/inspection tool while the proposed Firmware Engineering Studio is developed.
- Existing BoardView importer, Component workspace, Smart Backup, Batch Operations, Workflow Automation, Workshop Manager and Service Reports remain canonical.
- Existing Apple Center remains separate from Android and will not be folded into Android service modes.

## Service architecture required for real implementation

Each specialist engine must be a provider with a declared capability boundary. A provider must publish:
- Supported manufacturers, exact model codes, board revisions, chipsets and regional variants.
- Supported transport modes and the exact evidence required to identify them.
- Protocol and tool versions, firmware package types, loader/DA requirements and authorization requirements.
- Supported operations (read-only, backup, restore, diagnostics), required technician permissions and confirmation rules.
- Firmware source, cryptographic digest, publisher-signature result, compatibility evidence and known limitations.
- Automated test fixtures plus physical validation records for the exact model, revision, Windows build, driver, cable and adapter.

The future provider families are:
- BrandProfileProvider: exact OEM model and regional identity rules.
- TransportProvider: ADB, Fastboot, Windows PnP, Apple Lockdown and validated service transports.
- FirmwareFormatProvider: parser for one documented package format.
- ServiceProtocolProvider: one verified OEM/chipset protocol, never a generic guessed flasher.
- StorageAdapterProvider: supported eMMC/UFS/JTAG/ISP adapter and read/write capabilities.
- BoardDataProvider: licensed/source-backed boardview and component map.
- MeasurementProvider: typed readings from compatible instruments with units and calibration metadata.

Providers are registered through versioned interfaces and explicit capability metadata. The existing PluginHost/AppContainer and publisher-trust boundaries remain mandatory; adding a plugin manifest does not itself authorize native code or hardware access.

## Safety and ownership rules

SoftBox will support legitimate servicing of devices the technician is authorized to service. The product will not expose generic actions for:
- Bypassing FRP, Apple Activation Lock, MDM, account locks or user screen-lock authentication.
- Extracting PINs, passwords, patterns or protected user data by defeating device encryption.
- Changing, cloning or spoofing IMEI, serial number, MEID or other device identity values.
- Writing unknown EFS/NVRAM/RPMB/security partitions or loading an unknown Firehose/DA/loader.
- Flashing a package when exact model, board, region, rollback or signature evidence is unknown.

Supported alternatives include official owner/account recovery, carrier-authorized SIM unlock, OEM-approved bootloader unlock where available, owner-approved backups and restores, and documented firmware recovery. A reset or restore must show data-loss consequences and require explicit confirmation.

## Implementation sequence

### Phase A — foundation and model coverage
Build a normalized capability matrix keyed by manufacturer, exact model, board revision, chipset, region, firmware/build, transport mode and protocol version. Add provenance, confidence, source URL, licence, reviewer and last-validation metadata. Keep unknown values explicit.

### Phase B — firmware and partition engineering
Expand the existing Firmware Center with one parser per format, signature/trust reporting, archive limits, partition-map comparison and model-specific compatibility preflight. Build fixture-based parser tests before any service write path.

### Phase C — chipset read-only providers
Implement read-only discovery and information providers first for Qualcomm, MediaTek, Unisoc, Rockchip/Allwinner and Huawei/Honor. Do not enable flash merely because a VID/PID or boot mode is recognized.

### Phase D — owner-authorized recovery
Add backup/readback and restore planning with exact device and package binding, independent SHA-256 rechecks, trusted publisher evidence, power/connection requirements, explicit operator confirmation, audit records and post-operation verification. Destructive paths stay blocked if any required evidence is Unknown.

### Phase E — storage and board repair
Add read-only eMMC/UFS identity and health inspection, supported adapter profiles, image verification and source-backed board maps. Add writing only for explicitly supported, model-tested adapters and operations with rollback evidence.

### Phase F — diagnostics and service operations
Build Network/Modem/RF diagnostics, customer data rescue with consent, a durable per-device job queue, cancellation/resume, session isolation, service templates and evidence bundles.

## Acceptance criteria

A section is not marked Implemented because its navigation screen exists. An operation is not marked Tested because a mocked device answered. A feature is not Verified until a real target phone/board/adapter and exact service mode are tested with saved evidence.

Each release must report:
- Catalog and capability-matrix counts.
- Automated test counts and failing tests.
- Models/protocols physically validated.
- Unsupported or unknown combinations.
- Firmware sources and signature status.
- Driver/Windows/adapter compatibility.
- Evidence and audit record locations.

The 13 new sections remain Planned until their first real service provider is implemented and tested. Existing functionality remains available throughout the expansion.
