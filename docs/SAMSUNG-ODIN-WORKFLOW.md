# Samsung Odin workflow in SoftBox

## Included capabilities

- The Android Service Center exposes a Samsung Download Mode action only when the currently selected device profile reports Samsung.
- Immediately before the ADB reboot request, SoftBox checks the selected serial state and reads both `ro.product.manufacturer` and `ro.product.brand`. It sends `adb -s <serial> reboot download` only when at least one property identifies Samsung.
- The Samsung Tools section opens an Odin Assistant. It accepts a user-provided Windows Odin executable and local `.tar` / `.tar.md5` packages for BL, AP, CP and one of CSC or HOME_CSC.
- The assistant validates slot selection, checks file existence and size, and calculates SHA-256 from each selected package and the executable before presenting a manual launch confirmation.
- Fastboot Manager also supports a selected-device `fastboot reboot recovery` request.

## Important boundary

Odin is an external proprietary Windows application. SoftBox does not redistribute or download it, and does not assume undocumented command-line options. After launching Odin, the technician must manually load the selected packages into their corresponding slots, re-check device model/region/build and anti-rollback conditions, and operate Odin's own interface.

The assistant does not parse TAR contents, validate Samsung firmware signatures, prove model/region compatibility, check CSC policy, control Odin's Download Mode USB session, press Start, or report a completed flash. The SHA-256 values identify the bytes selected in this workstation only; they are not proof of publisher authenticity.

A normal CSC package may erase user data; HOME_CSC is intended to preserve user data in supported upgrade paths but is not a guarantee. A complete backup is required before any firmware operation. The technician remains responsible for selecting the correct package and understanding Odin's device-specific prompts.

No FRP, account lock, bootloader security, or device-owner protection bypass is provided.
