# Android Network Diagnostics

## Scope

SoftBox exposes a read-only Android network inspection tool in section 16 (Network Diagnostics). The tool requires one selected Android device that is currently authorized by ADB.

It reads only the following operating-system information through fixed argument lists:

- Global IPv4 and IPv6 addresses reported by Android's network interfaces.
- The default route, gateway (when present) and interface name (for example, wlan0 or rmnet).
- The global Wi-Fi enabled flag.
- The global airplane-mode enabled flag.

The service runs these ADB operations against the selected serial only:

- `ip route show default`
- `ip -o addr show scope global`
- `settings get global wifi_on`
- `settings get global airplane_mode_on`

## Privacy and limitations

- It does not read SSIDs, Wi-Fi passwords, application traffic, app-private data, browsing data or credentials.
- It does not scan ports, probe other LAN devices or change network configuration.
- Values are reported only when Android returns them. Unsupported commands, permission limits, missing interfaces or empty output are shown as unavailable, with command errors retained.
- Android vendors and releases may differ in command availability. A missing value is not evidence that the phone has no network connection.
- A reported default route/IP does not prove Internet reachability, DNS health, signal quality or mobile-data subscription status.
- The parser is covered by unit tests; no real handset was attached during this code change. The module remains Implemented, not Verified, until tested on physical devices and Android versions.

## Related tools

- Windows host interface, DNS and ICMP tests remain in the existing Network Diagnostics workspace.
- Wireless ADB pairing is a separate Device Center tool. It uses Android's pairing-code flow and requires the phone and computer on the same local Wi-Fi network.
