# Android Wireless ADB Pairing — SoftBox

## Requirements

- Android 11 (API 30) or later on the phone for the pairing-code workflow.
- Windows PC and phone connected to the same Wi-Fi network.
- Wireless debugging enabled by the phone owner under Developer options.
- SoftBox Windows package with the bundled Google Android SDK Platform-Tools (adb.exe).
- A reachable local network path between the phone and PC; firewall isolation may block the connection.

## Pairing procedure

1. Unlock the phone and open Settings → Developer options → Wireless debugging. Enable Wireless debugging yourself.
2. Choose Pair device with pairing code on the phone. Keep that screen open while the temporary code is valid.
3. In SoftBox, open Device Center → Wireless ADB.
4. Enter the IP address and pairing port shown in the phone's pairing dialog, plus the six-digit pairing code.
5. Enter the IP address and connection port shown on the main Wireless debugging screen. The pairing and connection IP/port endpoints are entered separately and are not assumed to be identical.
6. Press Pair and connect. SoftBox executes the real adb pair request first; it sends adb connect only after ADB confirms the pairing succeeded.
7. Return to Device Center. The normal ADB discovery loop should list the wireless device after the connection is available.

The pairing code is treated as temporary input: SoftBox does not store it in preferences or reports and clears the field when the operation finishes. ADB command output and the final connection endpoint are shown so the operator can see the actual result.

## Security and behavior

- The tool does not enable Developer options or Wireless debugging on the phone; the owner must do that on the device.
- It does not bypass Android authorization or pairing approval.
- The pairing IP/port and connection IP/port are entered separately because Android exposes separate pairing and connection endpoints.
- Pair and connect requests use the installed/bundled ADB executable through an argument list, not a shell command string.
- Pairing and connection commands have bounded timeouts and may be cancelled.
- The workflow is an authorized device operation and is not available to ReadOnly SoftBox accounts.
- A successful adb pair does not by itself prove the adb connect step succeeded; SoftBox reports each result separately.

## Troubleshooting

- Pairing code rejected: generate a new code on the phone and check the pairing port. Pairing codes expire.
- Paired but not connected: use the connection port shown on the main Wireless debugging page, not the pairing-dialog port.
- Connection times out: verify both devices are on the same Wi-Fi network and that the router does not enable client isolation. Check Windows firewall rules for ADB.
- No wireless device appears: refresh device discovery and confirm adb devices -l lists the wireless endpoint.
- Remove a paired PC: use the phone's Wireless debugging → Paired devices controls to forget/revoke that computer, or revoke debugging authorizations through Android Developer options.

## Verification status

- Automated tests cover endpoint validation, port separation, temporary code validation, command ordering, pair/connect failure and cancellation.
- The feature is classified Tested for automated protocol orchestration only.
- No physical handset test is claimed by repository CI. Record a physical-device verification separately for each relevant Android build and network environment.

## Official reference

- Android Debug Bridge (ADB): https://developer.android.com/tools/adb#wireless
