# SoftBox Plugin SDK — Manifest v1 and Native ABI v1

## Current implementation

SoftBox includes a version-1 manifest contract, ZIP package inspection, publisher trust/signature verification, signed read-only workflow execution, and a native DLL worker hosted in a temporary Windows AppContainer.

The public SoftBox.PluginSdk .NET project provides version constants, permission identifiers, a manifest model compatible with the internal PluginManifest v1 fields, validation and JSON serialization for package authors. The native ABI header remains available at samples/NativePlugin/softbox_plugin.h.

The managed SDK is a package-authoring contract. It does not sign a package, add a publisher key to the trust store, grant runtime permissions, or execute a plugin. Runtime execution remains controlled by the existing Plugin Store and isolated host.

Native execution requires a trusted publisher signature, exact re-inspection of the package at execution time, a root-level DLL entry point, and an empty requestedPermissions list. Native code runs in a temporary low-privilege Windows AppContainer with no network, ADB, USB, shared storage, host API or user-profile capabilities. A Job Object limits the worker to one process, 256 MiB memory, 15% CPU and 120 seconds. Input/output is JSON only (64 KiB input, 1 MiB output).

Inspection alone never executes code. The Plugin Store requires an explicit user click and confirmation before launching an accepted native package. This is process isolation, not a guarantee that arbitrary native code is safe; trust only publisher keys obtained through an independent channel.

## Public SDK project

- Project: src/SoftBox.PluginSdk/SoftBox.PluginSdk.csproj
- Target: .NET 10, without WPF or Android dependencies.
- API version: 1.0.0.
- Manifest schema: 1.
- Native ABI: 1.
- Contract model: SoftBoxPluginManifestV1 (same manifest fields and execution modes as the core v1 contract).
- Contract writer: SoftBoxPluginManifestWriter.Serialize.
- Contract validator: SoftBoxPluginManifestWriter.Validate.
- CI package: the Windows workflow builds `SoftBox.PluginSdk.1.0.0.nupkg` and uploads it as the `SoftBox-PluginSdk-NuGet` Actions artifact (unsigned; not yet published to nuget.org).

## Manifest field contract

Manifest v1 includes ManifestVersion, Id, Name, Version, ApiVersion, MinimumSoftBoxVersion, EntryAssembly, Publisher, Description, RequestedPermissions and ExecutionMode.

Native packages use a root-level DLL, and requestedPermissions must be empty. Read-only workflow packages use workflow.json and may request only device.read and device.diagnostics. Unknown permissions fail closed. The SDK also rejects duplicate permissions, unsafe entry paths, invalid semantic versions and oversized strings.

## Native plugin ABI v1

Native packages use a root-level DLL and an empty requestedPermissions array. The DLL exports:

- int __cdecl SoftBoxPluginGetApiVersion(void) — returns 1.
- int __cdecl SoftBoxPluginExecute(const uint8_t* requestUtf8, int requestLength, uint8_t* responseBuffer, int responseCapacity, int* responseLength) — reads UTF-8 JSON and writes UTF-8 JSON; return 0 on success.
- Optional void __cdecl SoftBoxPluginShutdown(void) — called just before unloading.

Buffers are owned by the host and valid only during the call; do not retain pointers. Responses must be valid JSON and no larger than 1 MiB. Dependencies must be root-level DLL files within the 80 MiB combined extraction limit.

The ABI header is in samples/NativePlugin/softbox_plugin.h. The sample manifest is illustrative and unsigned. Never place publisher private keys inside a plugin package.

## Package safety rules

The current inspector rejects unsafe paths, duplicate ZIP paths, symbolic links, packages larger than 100 MiB, expanded content larger than 100 MiB, entries larger than 50 MiB, suspicious compression ratios, missing manifests and missing entry assemblies. It computes SHA-256 over the exact package bytes.

SHA-256 is an integrity identifier, not proof of publisher identity. A valid manifest and safe ZIP layout do not imply that the plugin code is trustworthy.

## Still requiring release-level validation

1. Signing the SDK NuGet package, publishing it to a public feed and maintaining a versioned compatibility policy.
2. A documented external publisher-key distribution/revocation policy beyond the current local current-user trust-store add/remove operations.
3. Full Windows AppContainer integration coverage across supported Windows editions and hardened environments; hosted CI is not a substitute for every target machine.
4. Compatibility tests against independent third-party plugin builds.
5. Independent security review and an expanded malicious-package test corpus.

The Plugin Store includes an AppContainer self-test action. Native launch also runs this self-test automatically before loading plugin code. If the worker token does not report TokenIsAppContainer = 1, native execution is refused before plugin execution.
