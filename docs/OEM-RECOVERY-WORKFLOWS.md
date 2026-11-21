# OEM service and recovery workflows

This document describes the additive OEM workflow work on top of PR #21. It distinguishes code that can execute a supported command from vendor actions that still require a model-specific package, vendor authorization, or physical verification.

## Samsung Odin

- The existing Odin assistant validates the selected BL/AP/CP/CSC or HOME_CSC package plan, reads TAR/TAR.MD5 metadata, checks the appended MD5 and records SHA-256.
- SoftBox can launch the Odin executable selected by the technician and retain the existing BL/AP/CP/CSC or HOME_CSC package integrity checks.
- Samsung Download Mode is connected to the selected authorized Samsung ADB device through the existing guarded `adb reboot download` workflow; the live manufacturer/brand is rechecked before the command.
- A Windows UI Automation path inspects the exact selected Odin process path, its Odin window, one enabled Start button with an InvokePattern, visible package filenames and a COM port. It sends one Start Invoke only after the technician confirms firmware compatibility, backup and that the COM port belongs to the selected handset, and types the last four characters of the selected serial.
- After Start, the technician can request a read-only progress check. SoftBox reads only exposed UI Automation text and RangeValuePattern values, and reports PASS/FAIL only when the window visibly exposes such a token; an inaccessible or ambiguous Odin UI remains Unknown. It does not send additional keystrokes or commands.
- There are no coordinate clicks, simulated keystrokes or blind retries. If Odin does not expose the required accessibility data, Start stays blocked. SoftBox reports only that Start was invoked; the technician must still monitor Odin's Progress/Result fields.
- This is UI automation, not an official Samsung protocol or proof of firmware authenticity/device compatibility. Exact model, CSC, bootloader binary and rollback values remain technician-verified. Physical Download Mode/Odin testing across supported Odin versions and Samsung devices remains required.

## Xiaomi / Redmi / POCO Fastboot

- The ROM inspector reads .tar/.tar.gz/.tgz archives without extracting or running scripts. It reports products from android-info.txt, image names, flash partition names and destructive commands found in scripts.
- The Fastboot screen takes the selected package Product from the inspected archive, not from the phone's own Product value. The selected package Product must match a fresh Product read from the same Fastboot serial.
- A write is restricted to one local IMG with a filename matching the selected partition and a partition referenced by the inspected ROM's flash script. It is allowed only when the exact selected serial is present, Xiaomi identity is confirmed, the bootloader state is explicitly Unlocked, the operator confirms the OEM source and backup, and the live checks are repeated immediately before the command.
- The existing single-image action remains restricted to its explicit partition allow-list.
- A new full-ROM workflow accepts only an already-extracted ROM folder and exactly `flash_all.bat` or `flash_all_except_storage.bat`. It reads `android-info.txt`, requires exact live Product match, parses only direct Fastboot commands and creates an ordered plan; it never launches the BAT, SH, or other OEM executable.
- Every referenced image must resolve inside the ROM `images` directory, be a regular file under the configured size limits and pass SHA-256 before it is admitted to the plan. Each image is re-hashed and the exact serial, Product and unlocked Bootloader state are checked again immediately before its individual write.
- The interpreter rejects unlock/relock, erase/format of partitions other than the explicitly modelled userdata operation, OEM commands, arbitrary shell chaining, unknown commands, path traversal and protected data/calibration partitions such as metadata, persist, FRP, keystore, modemst, FSG/FSC and SSD. A userdata image may be written only from `flash_all.bat`.
- `fastboot -w` or `erase/format userdata` is represented as a separate destructive step, and is accepted only from `flash_all.bat`, not `flash_all_except_storage.bat`. It requires its own checkbox and a different typed confirmation. `flash_all_lock.bat` is always rejected.
- The tool executes each approved command separately against the exact selected Fastboot serial and stops at the first failed command. It does not cancel or close the window during an active write, and it does not automatically continue after an error. After a failed step the device may be partially flashed and must remain connected in Fastboot for technician recovery.
- Where both the ROM `images/anti_version.txt` and the device's numeric Fastboot `anti` value are available, SoftBox blocks a lower ROM index. When a comparable value is unavailable, the operator must acknowledge that Anti-Rollback was checked manually from OEM information. A region name is recorded as technician-provided confirmation, not independently authenticated by the app.
- Xiaomi bootloader unlocking remains a hand-off to Xiaomi's Mi Unlock tool. Account eligibility, waiting periods, data-loss warnings and approval remain with Xiaomi.

The package folder must be extracted by the technician before use. SHA-256 identifies the inspected bytes but does not authenticate Xiaomi as publisher, prove region compatibility or prove that every package component is OEM-signed. Real device/region-specific testing and recovery validation are still required before calling the workflow hardware-verified.

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
