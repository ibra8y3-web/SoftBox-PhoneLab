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
