# OEM service and recovery workflows

This document describes the additive OEM workflow work on top of PR #21. It distinguishes code that can execute a supported command from vendor actions that still require a model-specific package, vendor authorization, or physical verification.

## Samsung Odin

- The existing Odin assistant validates the selected BL/AP/CP/CSC or HOME_CSC package plan, reads TAR/TAR.MD5 metadata, checks the appended MD5 and records SHA-256.
- SoftBox can launch the Odin executable selected by the technician. It does not inject files into undocumented Odin controls, click Start, or claim that Odin is a publicly documented Samsung CLI.
- Samsung Download Mode entry remains the separately confirmed ADB reboot path.
- The Samsung USB driver and official support resources are linked in the Samsung section.
- **Remaining:** any full Odin automation needs a documented Samsung service interface, access granted by Samsung, exact model/CSC/bootloader/rollback data, and hardware validation. No GUI-click automation is advertised as a supported protocol.

## Xiaomi / Redmi / POCO Fastboot

- The ROM inspector reads .tar/.tar.gz/.tgz archives without extracting or running scripts. It reports products from android-info.txt, image names, flash partition names and destructive commands found in scripts.
- The Fastboot screen takes the selected package Product from the inspected archive, not from the phone's own Product value. The selected package Product must match a fresh Product read from the same Fastboot serial.
- A write is restricted to one local IMG with a filename matching the selected partition and a partition referenced by the inspected ROM's flash script. It is allowed only when the exact selected serial is present, Xiaomi identity is confirmed, the bootloader state is explicitly Unlocked, the operator confirms the OEM source and backup, and the live checks are repeated immediately before the command.
- The restricted list currently excludes vbmeta, erase, format, relock and arbitrary commands.
- Xiaomi bootloader unlocking is a hand-off to Xiaomi's Mi Unlock tool. Account eligibility, waiting periods, data-loss warnings and approval remain with Xiaomi.

The manual IMG selection is still an operator-controlled hand-off: the UI records SHA-256 and asks the technician to confirm that the extracted image came from the official package. It does not cryptographically prove that an arbitrary IMG file was extracted from the archive.

## Qualcomm EDL / QDL

- The workflow launches a Windows qdl.exe supplied/built by the operator from Qualcomm's public QDL source; SoftBox does not redistribute an unknown Firehose programmer or an unverified Windows binary.
- It checks for a present Qualcomm USB vendor device, uses QDL's device list and chipinfo commands, and requires the selected EDL serial to be present before reading chip information and again immediately before flashing.
- The technician must choose the storage type from the OEM package documentation (eMMC, UFS, NAND, NVMe or SPI-NOR); it is not guessed from a chip name.
- The package inspector reads the Firehose programmer and rawprogram/patch XML with DTD/external entities disabled, resolves image paths only inside the package directory, validates sector fields and hashes every referenced image.
- A successful QDL dry-run is mandatory before the write button is enabled. The actual write requires a previous non-placeholder model number, board and SoC snapshot, typed model/serial confirmation, OEM-package/authorization/backup acknowledgements and a fresh QDL list/chipinfo check.
- QDL and the device still enforce their own programmer/security requirements. SoftBox does not bypass Secure Boot, DAA, or OEM authentication.

**Remaining:** use an OEM-authorized programmer and storage-specific package for the exact model/board, verify with an authorized physical device, and run the end-to-end QDL Hardware-in-the-Loop workflow. A successful source build or XML dry-run is not proof of a successful phone recovery.

## MediaTek BROM / Preloader

- SoftBox parses the actual scatter file, captures platform/project and per-partition metadata, rejects path traversal, checks all download images and calculates SHA-256 for the scatter and each image.
- The preflight hashes the operator-supplied Download Agent and SLA/Auth files, then rechecks their hashes and re-inspects the scatter and image hashes immediately before hand-off.
- Exact Model Number and Hardware SKU values must be available from the previous device snapshot.
- SoftBox opens the SP Flash Tool executable chosen by the technician only after explicit acknowledgements and typed confirmation.

**Remaining:** this is not an automated BROM/Preloader flashing session. SoftBox does not ship a generic DA, authenticate DA signatures, determine SLA/DAA permission, bypass authorization or click Download. Those requirements are OEM/model/board-specific. A real supported flow needs a vendor-signed DA/Auth package, verified hardware matching and physical validation.

## Android OEM tool directory

The Brand section opens first-party resources where they exist, including Google's Android Flash Tool/factory-image resources, Motorola Software Fix and official Qualcomm/MediaTek documentation. A public support page is not proof that the tool supports every handset. No private OPPO, vivo, realme, Huawei/HONOR or other proprietary protocol is invented.

## Device Intelligence, Phone Link Studio and Update Center

- Device Intelligence retains evidence-based model/board coverage. An empty Board ID, community identity or BoardMapId reference is not promoted to verified PCB compatibility.
- Phone Link Studio continues to manage up to four independently hosted scrcpy sessions and revalidates ADB authorization. Physical multi-device/cable/codec/hotplug verification remains open.
- Update Center retains Stable/Beta release selection, signed package validation, staging and rollback helpers. The production publishing key and a signed production release remain external maintainer steps: configure the repository secret `SOFTBOX_RELEASE_PRIVATE_KEY_PEM`, independently publish the matching public-key fingerprint, import the public key in the installed app and validate an actual tagged release.

## Validation labels

- **Tested** means automated tests or static workflow checks passed.
- **In Development** means the code path is implemented but depends on an external tool/package or has not passed required hardware validation.
- **Planned** means the documented OEM protocol/tooling is not available for implementation yet.
- No item is labelled Verified merely because GitHub Actions built the desktop application.
