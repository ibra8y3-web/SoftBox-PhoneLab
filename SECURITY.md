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


## Local database protection

- The local account/audit database, workshop database, and device activity/notification store use Windows DPAPI with **CurrentUser** scope and distinct purpose-derived entropy. A different Windows account or a different data purpose cannot decrypt the file through the application. Passwords remain PBKDF2-HMAC-SHA256 verifiers; DPAPI adds protection for the verifier database and audit metadata at rest.
- Existing plaintext JSON stores are migrated on first successful read: SoftBox deserializes and validates the old document, then atomically rewrites it in protected form. If DPAPI cannot unlock an encrypted file, SoftBox reports the error; it must not silently reset the database.
- Manual workshop backups use the same protected format and the .sbxbackup extension. They are bound to the same Windows user context and are not portable between accounts or machines. Preserve the Windows profile and its DPAPI recovery capability before relying on these backups.
- CSV, HTML and JSON reports explicitly exported by the operator remain separate files and may contain customer/device data. Review and protect them before sharing.
