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

## Official recovery case builder

The Device Access & Official Recovery window includes an executable case builder for all six tracks listed above. It validates the operator's ownership/repair-authorisation declaration; MDM cases additionally require an organisation-administrator declaration, and carrier-unlock cases require the carrier-account-holder/delegate declaration. Manufacturer and model are required. A successful run writes a local Markdown checklist through the Windows Save dialog, with the official steps and source links for that case. The user submits the case to the OEM, organisation administrator or carrier themselves; SoftBox does not submit requests or claim approval.

The builder deliberately has no IMEI, serial-number, account-password, PIN, recovery-code or OTP input. Carrier identifiers must be entered directly into the carrier's secure official channel. It does not erase data or change device, account, MDM, modem or network state. This is a real case-file/export workflow, not a lock-bypass engine.

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


## Extended mobile network diagnostics — 21 November 2026

The same read-only window now also requests:

- `adb -s SERIAL shell dumpsys telephony.registry`

A separate parser extracts only these allow-listed fields where the OEM Android build exposes them:

- Voice and data registration states: in service, out of service, emergency only, radio powered off or unknown.
- Voice and data radio technologies: for example LTE, LTE_CA, NR, UMTS, GSM or an unknown/unmapped value.
- Roaming, emergency-only and network-search indicators.
- Public operator display label.

The parser never stores the raw telephony dump in its report model and has no output properties for IMEI, IMSI, ICCID, phone number, subscriber ID or SIM serial. Those unrelated fields may occur in vendor dumps, so the window only displays the parser's selected values. The complete diagnostic output is not exported or persisted by this feature.

Registration state is not an unlock result. A device can be registered on a carrier while still being subject to SIM/network restrictions in another context, and an out-of-service state can have many causes unrelated to a network lock. The carrier remains the authority for IMEI eligibility and network unlock approval.

Automated tests cover standard Android registration codes, LTE/NR technology labels, missing and conflicting fields, denied output, and the absence of subscriber identifiers from the parsed result. OEM variations and dual-SIM behavior require further physical validation.


## Management-policy evidence inspector — 21 November 2026

The Device Policy Manager result also extracts a bounded set of `ComponentInfo{package/class}` names and tokens matching Android's `no_*` user-restriction naming convention. The window labels them as components and restriction tokens *observed in the dump*. It does not assert that every displayed restriction is currently enforced for the selected user, and it never changes policy state or removes the Device Owner/Profile Owner.

Only component names and bounded token strings are returned; the complete Device Policy Manager dump is not exported or persisted by this feature. Missing or permission-denied output returns empty evidence lists and remains inconclusive. Physical validation across Android and OEM versions is still required.
