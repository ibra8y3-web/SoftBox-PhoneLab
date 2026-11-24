# Download Mode and Android service-mode recognition

## Why the Android list loses the phone

The Android runtime list uses `adb devices -l`. When a device is deliberately moved into Samsung Download Mode, Fastboot/Bootloader, Qualcomm EDL or a MediaTek Preloader/BROM interface, Android's ADB transport may no longer exist. The absence of an ADB row is therefore not the same as a missing USB device.

## Separate detection path

SoftBox's **Download & Service Mode Center** queries Windows SetupAPI/Configuration Manager through the existing read-only `WindowsUsbInventoryService`. It does not ask ADB whether the service-mode device exists. The center refreshes the Windows PnP inventory every two seconds while open.

Recognized interfaces include:

- Samsung `04E8:685D`: Samsung Download-mode CDC composite USB interface.
- Android USB descriptors explicitly identifying Fastboot or Android Bootloader Interface.
- Qualcomm `05C6:9008`: Qualcomm EDL / QDLoader 9008 interface hint.
- MediaTek Preloader/BROM and Download-Agent interfaces when their Windows descriptors or known interface identifiers are exposed.

Samsung `04E8:6860` is not labelled Download Mode. The exact phone model is not inferred from the Download-mode VID/PID. A COM port is displayed only as a Windows descriptor hint; it is not promoted to a device serial or automatically associated with an ADB identity.

## Firmware workflow

Select the detected USB interface and open **Firmware Center**. The detected manufacturer and service mode are copied as context, but the exact model remains blank if Windows did not disclose it. Verify the model suffix, region/CSC, bootloader binary/anti-rollback level and firmware source through authoritative service information before downloading or preparing a package.

Samsung Download Mode can open the existing Odin Assistant for local TAR/TAR.MD5 inspection and package preparation. The Odin Start control remains gated by the existing evidence workflow; this discovery context alone does not authorize a flash.

## Driver and transport diagnostics

If Windows PnP shows the device with a problem code or no suitable driver service, use USB Doctor / Driver Center and select a vendor driver that matches the exact hardware IDs. Do not replace a Samsung driver with WinUSB or replace a Qualcomm COM driver with WinUSB unless the selected tool explicitly requires that backend. Driver substitution can make other service tools lose access to the device.

The discovery center does not install or replace drivers automatically. It does not unlock bootloaders, bypass account locks, erase partitions, load unsigned programmers, or flash firmware.

## Verification status

Unit tests cover recognized interface classification, separation of Samsung normal USB `04E8:6860` from Download Mode `04E8:685D`, service-mode inventory selection and COM-port parsing. Real-phone validation still requires physical Samsung, Fastboot, Qualcomm and MediaTek devices with the relevant Windows drivers installed.
## Start authorization and audit

The Download Mode entry point passes the same authorization-and-audit service into the Odin Assistant. The irreversible Start action performs a second, dedicated `ExecuteSamsungOdinFlash` permission check after final confirmation; the action is recorded in SoftBox's local security audit store before UI Automation is allowed to invoke Odin. Missing authorization, a failed audit write, or an unconnected audit service blocks Start. This remains independent of ADB and does not weaken the same-record PnP/COM evidence requirement.
