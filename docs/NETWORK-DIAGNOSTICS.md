# Network Diagnostics — Local Windows Host

## Implemented scope

- Reads network interfaces exposed by Windows through `NetworkInterface.GetAllNetworkInterfaces()`.
- Shows operational state and type, IPv4/IPv6 unicast addresses, gateway addresses and DNS server addresses.
- Resolves a single user-entered DNS host or IP address using the operating system resolver.
- Sends one ICMP echo request to one selected resolved address, preferring IPv4 when available.
- Reports DNS addresses separately from ICMP status, round-trip time and TTL.
- Uses bounded DNS and ping timeouts and supports user cancellation.
- Rejects URL schemes, paths, query strings, whitespace and malformed host input.

## Safety and privacy boundaries

- No port scanning, subnet scanning, packet capture, credential collection or remote data upload is implemented.
- No network probe occurs merely by opening the window; DNS and ICMP are only requested after the user submits a host.
- The interface list is read from the local Windows host. It is not a reading of the connected phone's private Wi-Fi state.
- Android network observations and ADB Wireless pairing remain Planned until implemented with an authorized device channel and explicit user action.
- A firewall or router may block ICMP while DNS resolution works. Ping failure is not reported as DNS failure or proof that the network is offline.
- Interface addresses and DNS server values may be sensitive; review them before sharing screenshots or logs.

## Test coverage

`NetworkDiagnosticsServiceTests` covers accepted DNS/IP input and rejection of URL/path/malformed host values without depending on external DNS or internet availability.

Successful compilation and execution in CI do not replace live network testing in each Windows/network environment.

## CI evidence — 20 November 2026

PhoneLab CI run #57 completed successfully: restore passed; 51 tests passed, 0 failed and 0 skipped; Windows x64 self-contained publish succeeded; Android Agent and offline tool bundling succeeded.

Run: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44753337114

The tests validate input handling only and do not perform external DNS or ICMP calls. CI success does not replace testing on each Windows network environment or with a real Android handset.