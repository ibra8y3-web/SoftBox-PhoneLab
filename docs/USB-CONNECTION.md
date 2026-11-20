# USB connection and first-time authorization

PhoneLab requires Android Debug Bridge (ADB), not only Windows file transfer (MTP).

## First connection

1. Open Settings > About phone and tap Build number seven times (wording varies by manufacturer).
2. Open Developer options and enable USB debugging.
3. Unlock the phone, connect a data-capable USB cable and select File Transfer if asked.
4. Accept Android's **Allow USB debugging?** prompt. Choose **Always allow from this computer** only for a trusted PC.
5. PhoneLab detects the authorized ADB device, embeds the live display and installs/updates the bundled Agent APK.
6. PhoneLab Agent displays a second consent dialog for this session. The local bridge stays stopped until you tap **Allow this connection**.

## If File Explorer sees the phone but PhoneLab does not

- MTP/file transfer alone does not authorize ADB.
- Unlock the phone and accept the USB debugging prompt.
- Try another data-capable cable and USB port.
- Some Samsung, Xiaomi, Oppo, Vivo and other phones need their Windows ADB USB driver even when MTP works.
- Revoke old USB debugging authorizations in Developer options, reconnect and approve this computer again.
- Click **USB setup** in PhoneLab to restart ADB and retry.

## Offline package

The single Windows artifact includes the desktop app, Google Platform-Tools (ADB), verified scrcpy 5.0.1, verified TRACE Boardviewer 1.3.3 and PhoneLab Agent APK. These tools do not self-download on first launch. Optional model-catalog refresh and online repair-guide search still need internet.

## Security

- Agent binds only to 127.0.0.1:27831; Windows reaches it using ADB forwarding, not Wi-Fi/LAN.
- The bridge starts only after explicit user consent. Leaving the Agent Activity stops the server.
- Remote commands are allow-listed; no arbitrary shell command endpoint exists.
- USB debugging grants powerful developer access; only authorize trusted PCs.
