# SoftBox Plugin SDK — Initial Manifest Contract

## Current supported scope

This repository contains the version-1 plugin manifest contract, a local ZIP package inspector, signed data-only workflows and a signed native DLL worker hosted in Windows AppContainer.

The native execution path requires a trusted publisher signature, an exact re-check of the package at execution time and an empty requestedPermissions list. Native code runs in a temporary low-privilege Windows AppContainer with no network, ADB, USB, shared storage, host API or user-profile capabilities. A Job Object limits the worker to one process, 256 MiB memory, 15% CPU, and 120 seconds. Input/output is JSON only (64 KiB input, 1 MiB output).

Inspection alone never executes code. The Plugin Store requires an explicit user click and confirmation before launching an accepted native package. This is process isolation, not a guarantee that arbitrary native code is safe; trust only publisher keys obtained through an independent channel.

## Package layout

A package is a ZIP archive with one root manifest and one declared entry assembly:

\`\`\`text
sample.sbxplugin
├── softbox-plugin.json
└── Sample.Plugin.dll
\`\`\`

The entry assembly in this example is a placeholder for packaging/documentation only. SoftBox does not load it.

## Manifest example

\`\`\`json
{
  "manifestVersion": 1,
  "id": "org.softbox.sample",
  "name": "Sample Diagnostics",
  "version": "1.0.0",
  "apiVersion": "1.0.0",
  "minimumSoftBoxVersion": "0.1.0",
  "entryAssembly": "Sample.Plugin.dll",
  "publisher": "Example Publisher",
  "description": "Read-only sample extension manifest.",
  "requestedPermissions": [
    "device.read",
    "device.diagnostics",
    "reports.create"
  ]
}
\`\`\`

## Available permission identifiers

- \`device.read\`
- \`device.diagnostics\`
- \`storage.shared.read\`
- \`storage.shared.write\`
- \`reports.read\`
- \`reports.create\`
- \`notifications.create\`

Unknown permissions fail closed. The current manifest validator does not grant permissions. Sensitive actions such as raw shell execution, firmware flashing, authentication bypass, FRP bypass, IMEI/serial changes and private app-data access are not part of the plugin permission catalogue.

## Package safety rules

The current inspector rejects unsafe paths, duplicate ZIP paths, symbolic links, packages larger than 100 MiB, expanded content larger than 100 MiB, entries larger than 50 MiB, suspicious compression ratios, missing manifests and missing entry assemblies. It computes SHA-256 over the exact package bytes.

SHA-256 is an integrity identifier, not proof of publisher identity. A valid manifest and safe ZIP layout do not imply that the plugin code is trustworthy.

## Before a plugin runtime can be enabled

A production runtime still needs:

1. A publisher signing format and configured trusted root.
2. Signature verification over the package and manifest.
3. Explicit user permission grants with revocation.
4. Dependency and minimum-host-version resolution.
5. Process isolation and a restricted IPC contract.
6. Runtime/API compatibility tests and an uninstall/rollback lifecycle.
7. Security review and malicious-package test corpus.

Until all of these are available and tested, inspect-only behavior is intentional.

## Native plugin ABI v1

Native packages use a root-level DLL and an empty `requestedPermissions` array. The DLL exports:

- `int __cdecl SoftBoxPluginGetApiVersion(void)` — returns `1`.
- `int __cdecl SoftBoxPluginExecute(const uint8_t* requestUtf8, int requestLength, uint8_t* responseBuffer, int responseCapacity, int* responseLength)` — reads UTF-8 JSON and writes UTF-8 JSON; return `0` on success.
- Optional `void __cdecl SoftBoxPluginShutdown(void)` — called just before unloading.

Buffers are owned by the host and valid only during the call; do not retain pointers. Responses must be valid JSON and no larger than 1 MiB. Dependencies must be root-level DLL files within the 80 MiB combined extraction limit.

The ABI header is in `samples/NativePlugin/softbox_plugin.h`. The example manifest is illustrative and unsigned. Never place publisher private keys inside a plugin package.


The Plugin Store includes an **اختبار بيئة العزل** action. Native launch also runs this AppContainer self-test automatically before loading the plugin DLL. If the worker token does not report `TokenIsAppContainer = 1`, native execution is refused before plugin code is loaded.
