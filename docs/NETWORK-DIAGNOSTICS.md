# Network Diagnostics — Windows Host and Android

## Implemented scope

- Reads network interfaces exposed by Windows through `NetworkInterface.GetAllNetworkInterfaces()`.
- Shows operational state and type, IPv4/IPv6 unicast addresses, gateway addresses and DNS server addresses.
- Resolves a single user-entered DNS host or IP address using the operating system resolver.
- Sends one ICMP echo request to one selected resolved address, preferring IPv4 when available.
- Reports DNS addresses separately from ICMP status, round-trip time and TTL.
- Uses bounded DNS and ping timeouts and supports user cancellation.
- Rejects URL schemes, paths, query strings, whitespace and malformed host input.
- Reads public Android network interface addresses using `adb -s <serial> shell ip address show`.
- Reads public Android IP routes using `adb -s <serial> shell ip route show`.
- Android reads are scoped to the currently selected, ADB-authorized device; both commands use bounded timeouts, cancellation and a 64 KiB output cap.
- The UI reports actual command output and distinguishes unavailable/empty output from successful observations. It does not infer Wi-Fi signal, Internet access or network health from an address alone.
- Wireless ADB pairing is implemented as a separate tool in Device Center; it is not conflated with reading Android network interfaces.

## Safety and privacy boundaries

- No port scanning, subnet scanning, packet capture, credential collection or remote data upload is implemented.
- No network probe occurs merely by opening the window; DNS and ICMP are only requested after the user submits a host.
- The Windows interface list is read from the local computer.
- Android observations are read only from the selected authorized phone through standard ADB shell commands that expose interface addresses and IP routes. They do not reveal Wi-Fi passwords, app-private data or hidden vendor radio state.
- If the device does not expose the standard `ip` utility or ADB authorization is unavailable, the UI shows the real command failure rather than a fabricated result.
- Android network reads require an authorized ADB connection. Wireless pairing itself is handled in Device Center and requires the user to enable and approve Wireless debugging on the phone.
- A firewall or router may block ICMP while DNS resolution works. Ping failure is not reported as DNS failure or proof that the network is offline.
- Interface addresses and DNS server values may be sensitive; review them before sharing screenshots or logs.

## Test coverage

`NetworkDiagnosticsServiceTests` covers host input validation without external DNS/Internet dependencies. `AndroidNetworkDiagnosticsServiceTests` covers selected-device targeting, interface/route output handling, invalid serial rejection (including option-like `-s`), ADB unavailability, empty output and cancellation.

Successful compilation and execution in CI do not replace live network testing in each Windows/network environment.

## CI evidence — 20 November 2026

PhoneLab CI run #57 completed successfully: restore passed; 51 tests passed, 0 failed and 0 skipped; Windows x64 self-contained publish succeeded; Android Agent and offline tool bundling succeeded.

Run: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44753337114

The tests validate input handling only and do not perform external DNS or ICMP calls. CI success does not replace testing on each Windows network environment or with a real Android handset.