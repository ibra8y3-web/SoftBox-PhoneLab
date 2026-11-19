# Automatic device identification

PhoneLab combines multiple Android identifiers rather than trusting one display name:

- ro.product.manufacturer and vendor/system variants
- marketing model and model number
- product name and device codename
- board/platform identifier and hardware SKU
- build fingerprint, bootloader, SoC and ABI

The bundled identity catalogue contains curated starter profiles and is updateable from this repository. A match does not imply that an exact internal board map exists.

A board map is accepted as model-accurate only when it has an exact model number, device code or catalog map ID; isVerifiedForModel true; a calibrated accuracy level (pcb, boardview or manufacturer); an HTTPS provenance URL; source name; reviewer and verification timestamp.

The generic exploded view is never elevated to an exact map. No phone manufacturer's board coordinates are fabricated when no licensed/verified source is available.

## Device profile contributions
Add model identifiers to data/device-profiles/catalog.json. Keep model numbers, device codes, product IDs and aliases separate. Include a manufacturer/product source. Do not set identityVerified for guessed aliases.

## iFixit repair search
PhoneLab can query iFixit's public repair search on demand and open the original guide. It does not scrape or mirror the repair corpus or claim that iFixit guide images are PCB boardviews. Observe iFixit's current API key, attribution and commercial-use terms before distributing a commercial product.
