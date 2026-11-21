# Device access diagnostics and official recovery

This module adds an evidence-limited Android read-only diagnostic and official recovery directory. It is intentionally not a lock-bypass suite.

## Implemented read-only device inspection

For the exact ADB-authorized serial selected in SoftBox, the desktop window executes a fixed set of commands:

- `adb -s SERIAL get-state` to re-check that the selected target is still authorized.
- `adb -s SERIAL shell dumpsys device_policy` to look for Device Owner, Profile Owner and Active Admin indicators.
- `adb -s SERIAL shell dumpsys window` to read known Keyguard-visibility fields, if the Android build exposes them.
- `adb -s SERIAL shell getprop gsm.sim.state` and `gsm.operator.alpha` to display basic SIM/operator properties.

The parser is conservative:

- Keyguard output describes whether the lock screen is reported as visible at the instant of the read. It does not determine whether a PIN, password or pattern is configured, and it cannot recover credentials.
- Device Owner/Profile Owner/Active Admin indicators are evidence of device-management configuration. If no known marker is found, SoftBox explicitly says that this does not prove MDM is absent.
- Missing, conflicting, unsupported or permission-denied fields remain unknown or unavailable.
- SIM state and operator name do not establish whether a SIM/network lock is present or whether an IMEI is eligible for unlocking.
- The commands are read-only. The module does not reset, wipe, flash, unlock, disable services, modify accounts, edit policy, access private account databases, or write modem/NV data.

The window is available under **Security & Privacy → فحص حالة شاشة القفل ومؤشرات إدارة الجهاز وخصائص SIM**. It remains useful as an official recovery directory even when no ADB-authorized phone is connected.

## Account, lock and carrier cases

| User-requested tool | SoftBox status | Supported path |
|---|---|---|
| Google FRP Bypass | Unsupported | Recover the Google account previously synced to the device or contact the OEM/authorized service route with ownership evidence. |
| Xiaomi Mi Account Bypass | Unsupported | Xiaomi account password recovery or the official locked-device appeal. |
| Samsung FRP Bypass | Unsupported | Recover the Google/Samsung account or use an authorized Samsung service center with proof of purchase. |
| PIN / Pattern / Screen Lock bypass | Unsupported | Manufacturer account recovery and the official recovery options available for the exact Android/One UI version. No credential extraction or lock removal. |
| MDM Bypass | Unsupported | The organization's Device Owner/EMM administrator must release or unenroll the device. |
| SIM / Network Unlock bypass | Unsupported | The original carrier must confirm IMEI eligibility and authorize the network unlock. SoftBox does not alter Baseband/NV state. |

The catalog displays these six entries as Unsupported with no Run action. Separate entries describe the official recovery hand-offs and are not presented as bypass functions.

## Official recovery entry points

- Google account recovery: https://accounts.google.com/signin/recovery
- Google device protection / FRP: https://support.google.com/android/answer/9459346
- Google screen-lock help: https://support.google.com/android/answer/7663172
- Samsung Account: https://account.samsung.com/
- Samsung authorized service locator (Egypt regional page): https://www.samsung.com/eg/support/service-center/
- Xiaomi Account recovery: https://account.xiaomi.com/helpcenter/faq/en_US/02.faqs/03.password/faq-1
- Xiaomi locked-device appeal: https://account.xiaomi.com/helpcenter/faq/en_US/01.guide/11.appeal/faq-6
- Android Enterprise / enterprise FRP guidance: https://support.google.com/work/android/answer/14549362

A link is a hand-off to the service owner, not an API integration or approval. SoftBox does not submit a Google, Samsung, Xiaomi, EMM or carrier unlock request automatically.

## Device coverage and validation

The diagnostics are generic ADB observations and can run on Android devices that authorize ADB and expose the requested shell services/properties. OEM builds may hide, rename or restrict these outputs; the result can be partial or unknown. No claim is made for every handset, Android release, iOS device, region, mobile carrier or MDM provider.

Automated parser tests cover known, missing and conflicting diagnostic strings. They do not replace physical-device validation across OEM builds. The module must remain **In Development** until the relevant Samsung, Xiaomi, Pixel, Motorola and other supported test devices are physically checked and evidence is recorded.
