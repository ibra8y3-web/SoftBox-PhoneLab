# Samsung Download Mode and Odin workflow in SoftBox

## Device discovery without ADB

- The Download Mode Center reads Windows USB Plug-and-Play records independently from ADB. ADB may disappear after the phone enters Download Mode; this does not remove the Windows USB evidence.
- Samsung Download Mode is classified from a known Samsung Download-mode USB interface or an explicit Windows descriptor. A vendor ID, PnP instance suffix or friendly name is not treated as the handset's exact model or serial number.
- When the technician opens Odin from a selected Download Mode record, SoftBox carries that exact PnP instance, Hardware ID, mode evidence and any COM port reported by that same record into the Odin Assistant.
- If the selected Windows USB record does not expose a COM port, the Download Mode Start path stays blocked. SoftBox does not accept a manually typed unrelated COM port, does not substitute the PnP suffix for ADB Serial, and does not claim a device-to-COM association that Windows did not expose.
- The exact Samsung model must be confirmed by the technician when ADB is no longer available. The model field is not silently inferred from VID/PID.

## Package inspection and Odin UI checks

- The technician selects a Windows Odin executable and local TAR/TAR.MD5 packages for BL, AP, CP and one of CSC or HOME_CSC. AP is required; CSC and HOME_CSC cannot be selected together.
- SoftBox checks TAR structure and any appended MD5, records SHA-256, then rechecks package hashes immediately before Start so changed or replaced files invalidate the inspection.
- Odin remains an external proprietary application supplied by the technician. SoftBox does not redistribute it or rely on undocumented command-line switches.
- Before Start, Windows UI Automation checks the selected Odin executable path and process, one enabled Start control with the supported InvokePattern, visible names of the selected package files, and the COM port shown in Odin.
- For a Download Mode session without ADB, the COM port visible in Odin must exactly match the COM extracted from the same selected Windows USB PnP record. For an ADB-identified Samsung session, the COM still must match the technician-selected target and the explicit acknowledgement.
- The technician must confirm model/region/bootloader binary/anti-rollback compatibility, a valid backup and that the COM port belongs to the selected phone. A final separate confirmation dialog is required before SoftBox sends one accessibility Invoke to Start.
- The same Odin process and COM port are checked again immediately before the Start invocation. SoftBox does not click screen coordinates or inject blind mouse/keyboard input.
- After Start, the monitor reads visible Odin status/progress only. A visible PASS/FAIL is an Odin UI result, not an independent verification of every written partition or successful Android boot.

## Important limitations

A TAR/MD5 check and SHA-256 identify the local bytes; they do not establish publisher authenticity, correct CSC, model/region compatibility, anti-rollback acceptance or OEM authorization. The technician must obtain the correct signed firmware from a trusted source and verify the exact model and build before proceeding.

A normal CSC package may erase user data. HOME_CSC is intended to preserve user data in supported upgrade paths but is not a guarantee. A complete backup is required before any firmware operation.

If Windows does not expose the COM port on the selected Samsung USB record, or Odin does not expose one unique Start control and the selected package names, SoftBox blocks Start. It does not replace missing evidence with a guess.

No FRP, account-lock, bootloader-security or device-owner protection bypass is provided. No IMEI/serial mutation or EFS identity rewrite is performed.
## Permission and audit gate

- Opening the Download Mode Center or Odin Assistant does not grant permission to start a flash. The Start handler independently checks the dedicated `ExecuteSamsungOdinFlash` action, which requires `DeviceOperations`.
- The authorization check runs after the technician's final confirmation and immediately before Start is invoked. The action must also be written to the local security audit store; if permission is denied, the audit write fails, or the audit callback is missing, SoftBox does not invoke Start.
- The audit event records an authorized Start invocation attempt. It does not mean that Odin completed the flash, that every partition was written, or that Android booted successfully. Those outcomes remain limited to the visible Odin UI evidence described above.
- A short-lived in-process guard rejects concurrent Start clicks so one confirmation sequence cannot dispatch duplicate UI Automation invocations.
