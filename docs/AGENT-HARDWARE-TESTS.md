# PhoneLab Agent hardware, battery and diagnostics

The Android Agent exposes hardware tests through its own UI and the Windows ADB bridge.

- Bluetooth: reads adapter presence and state, then opens Android Bluetooth settings for a technician/user-led pairing check. It does not silently scan nearby devices or store addresses.
- Wi-Fi: reads radio state and the active network transport, then opens Android Wi-Fi settings for a user-led connection check. It does not collect network names or passwords.
- NFC: uses Android reader mode and requires a real NFC tag within the timed test. It stores technology names only, not tag UIDs or tag contents.
- GPS: requests fine-location permission and waits for a GPS fix; it stores the reported accuracy only, not latitude/longitude or location history.
- Fingerprint: checks sensor/enrollment availability and invokes AndroidX BiometricPrompt. Biometric templates never leave Android's system service.
- Physical buttons: observes Volume Up and Volume Down while the test is active. Android does not expose ordinary app-level monitoring for Power/Home buttons; those are explicitly outside the result.
- SIM: reads the Android SIM state, active-modem count and operator label after a runtime permission request. IMEI, IMSI, ICCID and phone number are not read.
- Battery snapshots: WorkManager schedules a sample every 30 minutes; samples are stored locally in bounded JSONL. The most recent sample and sample count are included in the ADB snapshot and displayed in Windows device facts.
- Structured diagnostics: Agent writes bounded JSONL event and crash files in app-private storage. Windows can export the report only when the technician requests it via the local ADB-forwarded endpoint.

Hardware availability, Android permissions, OEM restrictions and the need for a real NFC tag / Bluetooth accessory / GPS sky view / enrolled fingerprint are surfaced as not_tested, review or successful observations. A software build or emulator run is not proof of physical sensor health.
