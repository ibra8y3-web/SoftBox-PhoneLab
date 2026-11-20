# SoftBox Plugin SDK — Initial Manifest Contract

## Current supported scope

This repository now contains the version-1 plugin manifest contract and a local ZIP package inspector.

The current release does **not** install or execute plugin assemblies. Publisher signatures, trusted signing roots, runtime isolation, permission prompts, dependency installation and uninstall lifecycle are not implemented. The inspector always reports:

- \`PublisherSignatureVerified = false\`
- \`CodeLoadedOrExecuted = false\`

Do not distribute a DLL as a working SoftBox plugin based on the manifest contract alone.

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
