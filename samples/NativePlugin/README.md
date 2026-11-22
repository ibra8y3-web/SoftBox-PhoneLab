# Native AppContainer plugin contract

Build a Windows DLL exporting `softbox_plugin.h`, include it at the root of a signed `.sbxplugin` archive, and set `requestedPermissions: []`.

The plugin receives and returns JSON only. It has no implicit access to SoftBox, connected phones, USB/ADB, credentials, user folders, shared storage or the network. A separate Windows AppContainer is launched with no capabilities and bounded by a Job Object.

The sample manifest is illustrative and unsigned. Publishers must keep their private signing key outside the package. Users should obtain publisher key fingerprints through an independent trusted channel.
