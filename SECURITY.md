# Security and privacy

- PhoneLab uses fixed ADB subcommands and argument-list process APIs; it does not concatenate user-provided shell commands.
- Current Android inspection is read-only.
- No FRP, Activation Lock, MDM, carrier lock or account-lock bypass is implemented.
- No device partitions are flashed, erased or modified.
- Agent bridge binds only to 127.0.0.1 on the phone and is reachable from Windows through an explicit ADB forward.
- Diagnostic snapshots remain on the local PC. The application does not upload phone contents to a cloud service.
- Reports can include device identifiers and measurements. Store and share them as sensitive service records.
- Do not include IMEI, IMSI, phone numbers, contacts, photos, messages or authentication tokens in issue reports.
- Report security vulnerabilities privately to the repository maintainer rather than publishing exploit details.
