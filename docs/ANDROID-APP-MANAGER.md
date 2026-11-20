# Android App Manager — SoftBox

## Available operations

- Reads the installed third-party package list from the selected authorized device using `pm list packages -3`.
- Reads the selected package's details through `dumpsys package`.
- Installs or updates a user-selected local APK only after local APK inspection and an explicit confirmation. Android still enforces package signature compatibility.
- Removes the selected third-party app through the native ADB `uninstall` command after an explicit confirmation.

## Removal safeguards

1. The package name must pass the Android package-name validation rule.
2. SoftBox re-reads the selected device's third-party package list immediately before removal.
3. Removal is rejected unless the exact package name appears in that list.
4. The user sees the exact device serial and package name, plus a clear warning that the app's local data will be removed.
5. SoftBox sends `adb -s <serial> uninstall <package>` only after the user confirms.
6. The UI reports success only when ADB returns its explicit `Success` result. Any other result remains a failure/unknown outcome.

The service does not accept arbitrary shell commands or a free-form package name without checking the device's current third-party package list. System packages that are not in `pm list packages -3` are rejected. The feature does not access private app data or bypass Android permissions.

## Requirements and limitations

- An ADB-authorized Android device selected in Device Center.
- The selected app must be listed by Android as a third-party package for that device.
- Uninstalling an app removes its local data and cannot be undone by SoftBox. Back up user data separately before continuing.
- Split APK/App Bundle deployment, managed-profile targeting, OEM system-app removal and app-private data management are not implemented by this workflow.
- Automated tests validate command sequencing and safeguards using a fake command runner in the isolated test project only. The production app uses the actual ADB executable.
- A physical-device test is still required before classifying the operation as hardware Verified.


## Split APK set installation

The App Manager can install one Android Base APK plus its configuration/feature split APKs using the native ADB `install-multiple -r` operation.

Before asking for confirmation, SoftBox:

1. Inspects each APK's ZIP entries and AndroidManifest locally.
2. Requires all selected APKs to have the same valid package name and Version Code.
3. Requires exactly one Base manifest without a `split` attribute.
4. Requires at least one additional APK with a non-empty, unique `split` identifier.
5. Rejects missing manifests, unreadable archives, duplicated local paths, duplicate split identifiers and inconsistent Version Name values when present.
6. Orders the Base APK first and the split APKs by split identifier.

The confirmation identifies the target ADB device, package, version and selected files. Immediately before executing ADB, SoftBox recalculates every file's SHA-256; changed files stop the operation. ADB success is reported only when output contains an explicit `Success` line.

SoftBox does not independently verify APK signing certificates. The Android package installer validates signer compatibility and may reject a split set whose APK signatures do not match. No signature bypass is implemented. This workflow does not access private app data or system package areas.

Tests exercise APK manifest parsing, package/version matching, base/split cardinality, split uniqueness, argument ordering, selected-device targeting, file mutation after inspection and explicit ADB success handling. Tests use local ZIP fixtures and an isolated fake ADB command runner; hardware verification still requires a real authorized device.
