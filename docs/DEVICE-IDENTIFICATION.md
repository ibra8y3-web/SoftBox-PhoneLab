# Automatic device identification

PhoneLab combines multiple Android identifiers rather than trusting one display name:

- ro.product.manufacturer and vendor/system variants
- marketing model and model number
- product name and device codename
- board/platform identifier and hardware SKU
- build fingerprint, bootloader, SoC and ABI

The bundled catalogue contains curated starter profiles. On update, PhoneLab also downloads the Apache-2.0 bsthen/device-models mapping, which is generated from Google's public Play supported-device list and refreshed daily upstream. The combined lookup maps exact device model codes to brand and marketing names. Community records remain 'likely' identity matches, not proof of the hardware revision or a boardview. Network failure preserves the last local cache. A match does not imply that an exact internal board map exists.

A board map is accepted as model-accurate only when it has an exact model number, device code or catalog map ID; isVerifiedForModel true; a calibrated accuracy level (pcb, boardview or manufacturer); an HTTPS provenance URL; source name; reviewer and verification timestamp.

The generic exploded view is never elevated to an exact map. No phone manufacturer's board coordinates are fabricated when no licensed/verified source is available.

## Device profile contributions
Add model identifiers to data/device-profiles/catalog.json. Keep model numbers, device codes, product IDs and aliases separate. Include a manufacturer/product source. Do not set identityVerified for guessed aliases.

## iFixit repair search
PhoneLab can query iFixit's public repair search on demand and open the original guide. It does not scrape or mirror the repair corpus or claim that iFixit guide images are PCB boardviews. Observe iFixit's current API key, attribution and commercial-use terms before distributing a commercial product.


## Additional model-code data source

- Dataset: https://github.com/bsthen/device-models
- Upstream description: model code to brand and marketing name, generated from Google's public Play supported-device list and updated automatically.
- License: Apache License 2.0 (see upstream repository).
- PhoneLab downloads and caches the JSON at runtime; it does not copy the whole upstream file into this repository.
- This data improves names/model-code matching only. It does not include per-variant motherboard component coordinates, PCB layers, IC positions or connector locations.


## Section 38 — Device Identity Integrity & Smart Auto-Fill

The separate identity workspace combines read-only observations from four independently sourced channels:

- **Windows USB PnP:** VID/PID, hardware IDs, friendly name, manufacturer and Windows problem code. VID/PID is an interface identifier, not a handset serial or proof of exact model.
- **Android ADB:** transport serial and authorization state from the current ADB inventory. An unauthorized state is shown as pending; it is not elevated into an authorized OS read.
- **Android Fastboot:** transport serial plus optional read-only `fastboot getvar all` product/variant/hardware-revision fields. These are Bootloader-reported values, not a cryptographic hardware authenticity result.
- **Apple Lockdown:** serial or UDID returned from the selected libimobiledevice toolset after the device has established a trusted Lockdown session. Recovery/DFU or USB-only presence is not treated as a full Lockdown identity read.

The Device Identity Integrity engine groups only exact identifiers of the same platform and declared identifier type. It marks single-source reads as insufficient evidence, vendor/VID hints for review, and conflicting manufacturer/model claims as a conflict. It does not assume that ADB and Fastboot serials are identical or that the same USB port/device at a nearby time proves one physical handset.

The selected read fills the section's manufacturer, model, mode and masked identifier fields automatically. The exact value is copied only after the technician explicitly presses Copy. Reports use masked values. No raw identifier is persisted by this section. Windows USB PnP instance-path suffixes are displayed separately as unverified hints because some are device serials and others are port/interface tokens; a heuristic never upgrades them into a verified serial.

**Not implemented by design:** changing or spoofing IMEI/serial, writing EFS/NVRAM/service partitions, defeating a block or changing a handset's identity. In the Samsung Odin window, the `FLASH` phrase is prefilled only from the last four characters of the exact Serial returned by an authorized ADB transport. This is convenience text only; USB PnP suffixes are never used as a substitute. Firmware compatibility, backup, COM-port ownership and the final Start confirmation remain manual. SoftBox does not auto-press Start. Official board/identity replacement remains an OEM-authorized service procedure.
