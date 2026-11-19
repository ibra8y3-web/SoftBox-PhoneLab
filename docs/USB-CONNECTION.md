# USB connection and first test

1. Install Google Android SDK Platform-Tools using the provided PowerShell helper or Google's official download.
2. Restart PhoneLab after setting the SOFTBOX_ADB environment variable.
3. On Android, open Settings > About phone and tap Build number until Developer options are enabled (exact labels differ by vendor).
4. Open Developer options and enable USB debugging.
5. Connect with a USB data cable. If Android asks for the USB mode, choose File transfer or the available data mode.
6. Approve the PC RSA debugging prompt on the phone. Do not approve unknown computers.
7. The Windows app lists the ADB device. A device marked unauthorized needs the on-phone approval.
8. Select the device and inspect the available read-only system properties.
9. For interactive tests, install and open the PhoneLab Agent APK. Keep it in the foreground.
10. In Windows, press Update Agent to import its test evidence.

Troubleshooting:
- Charge-only cable: replace with a known data cable.
- Device missing from ADB: install the phone vendor's USB driver on Windows where required.
- Unauthorized: unlock the phone and approve the RSA prompt; revoke old debugging authorizations only on the phone owner's instruction.
- ADB not found: run scripts/install-platform-tools.ps1 or add platform-tools to PATH.
- Agent unavailable: confirm the Agent is open on the phone and the correct ADB device is selected.
