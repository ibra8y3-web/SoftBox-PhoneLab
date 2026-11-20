# Wireless ADB Pairing

## What this tool does

SoftBox uses the official Android SDK Platform-Tools already bundled with the Windows package. It runs the authenticated pairing-code sequence:

1. `adb pair <phone-ip>:<pairing-port> <six-digit-code>`
2. `adb connect <phone-ip>:<wireless-debugging-port>`

The pairing IP and port are read from the temporary **Pair device with pairing code** dialog. The connection IP and port are read separately from the main **Wireless debugging** screen. The two endpoints may differ and must not be assumed to be identical.

## Phone requirements

- A phone running Android 11 (API 30) or later.
- Developer options and Wireless debugging enabled.
- The phone and Windows computer connected to the same Wi-Fi network.
- A current Android SDK Platform-Tools package with wireless pairing support.
- The user must approve Wireless debugging on the phone.

Official instructions: https://developer.android.com/tools/adb#connect-to-a-device-over-wi-fi

## SoftBox behavior

- The operator enters the pairing IP and port, the connection IP and port, plus the six-digit temporary code shown by Android.
- The service validates the IP, both port ranges and the code before starting ADB.
- SoftBox executes the existing ADB binary using argument-list process invocation; it does not invoke a command shell.
- The password field is cleared before ADB execution. SoftBox does not save the pairing code in preferences, logs or reports.
- Pairing and connection use bounded timeouts and can be cancelled.
- The ADB output is displayed. SoftBox reports success only when ADB returns recognized pairing and connection success output.
- After connection, the operator returns to Device Center and refreshes discovery; the wireless serial returned by ADB is then handled by the existing multi-device workflow.

## Limitations and verification

- The automated tests cover IP/port formatting, rejection of hostnames and malformed IPs, six-digit ASCII pairing-code validation and account-role authorization.
- No physical phone was attached during this repository change. Wireless pairing remains **Implemented**, not **Verified**.
- OEM network isolation, captive portals, firewall rules, VPN routing, Android vendor variations and stale/revoked pairing records can prevent connection.
- To revoke trust, use the phone's Wireless debugging > Paired devices > Forget menu or revoke debugging authorizations from Android settings.
- This feature uses the Platform-Tools already packaged by CI; it adds no separate runtime library or large binary payload.
