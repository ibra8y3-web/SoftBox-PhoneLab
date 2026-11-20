# USB transports, Android evidence and firmware inspection

## Transport inspection
The USB diagnostic screen invokes the bundled Google Android SDK Platform-Tools:
- \`adb devices -l\` lists devices visible to ADB, including the authorization state.
- \`fastboot devices\` lists devices visible to the generic Fastboot tool when the phone is already in that mode.

These are read-only inventory commands. PhoneLab does not reboot the phone into another mode, unlock a bootloader, erase partitions, alter FRP state or flash firmware.

MTP is a file-transfer protocol and does not grant the ADB diagnostic interface. Recovery, Samsung Download Mode, Qualcomm EDL and MediaTek Preloader are vendor/platform-specific interfaces. Generic ADB/Fastboot enumeration cannot guarantee detection of those modes. The Windows offline package currently includes the official Samsung USB driver as an optional installer; it does not include every OEM driver.

## Android evidence capture
For an ADB-authorized phone, the evidence exporter collects:
- the last 1,500 logcat lines in thread-time format;
- Android system properties;
- battery service state;
- thermal service state; and
- boot uptime.

This can help a technician correlate a reported restart with recorded exceptions, watchdog messages or boot-reason properties. The built-in boot-loop matcher is a text heuristic. It is explicitly a lead for review, not a confirmed diagnosis. An early boot failure may occur before Android can write logs.

**Privacy warning:** logcat and system properties can contain account labels, notification text, package names, file paths, device serials and application data. Inspect and redact logs before sharing.

## Firmware archive inspection
The inspector calculates SHA-256 and checks whether a ZIP contains common Android OTA elements such as \`META-INF/com/android/metadata\`, \`payload.bin\` or an updater binary. Non-ZIP files receive a hash and a basic format assessment.

A SHA-256 hash proves only the identity of the bytes that were inspected. It does not validate a publisher certificate, cryptographic OTA signature, device/region compatibility or safe installation. PhoneLab does not execute updater scripts or flash, unlock, wipe or otherwise modify the connected phone.

## Explicit non-goals
No FRP bypass, account-lock bypass, IMEI modification, exploit-based EDL access, unsigned programmer loading, automatic factory reset, bootloader unlocking or unattended firmware flashing is provided. Such operations can destroy customer data or damage a device and must not be inferred from a diagnostic result.
