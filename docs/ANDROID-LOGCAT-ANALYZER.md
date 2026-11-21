# Android Logcat Analyzer

The Android Service Center exposes a local, read-only Logcat analyzer for the currently selected ADB-authorized device.

## Operation

- Runs the fixed ADB preset `adb -s <serial> logcat -b all -d -v threadtime -t 3000`.
- Limits analysis to the latest 3,000 lines and a maximum of 8 MiB / 10,000 analyzed lines.
- Groups pattern matches into crash, ANR, memory, permission/security, SELinux, thermal/power, storage/filesystem, USB/connectivity, Android system and network categories.
- Displays counts, severity, tags, and last-seen time; exports a JSON summary.
- Cancels the ADB operation when requested and never changes device state.
- Does not export raw Logcat lines or upload anything.

## Limitations

The analyzer is a triage aid. Pattern matches are not proof of hardware damage, and absence of matches is not proof that the device is healthy. Vendor builds may redact logs. Review summaries before sharing because event categories and tags may still reveal application or system information.
