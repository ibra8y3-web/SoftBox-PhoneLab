# SoftBox component database

One JSON file per device model. SoftBox matches the connected phone (model number such as `SM-A315F`,
device code, or catalog profile id) and shows each part's published specification, confidence level and sources.

## Rules
* Record only facts you can point to. Unknown stays `"confidence": "unknown"`; never guess IC part numbers.
* `confidence`: `published` (two or more independent sources agree), `single-source`, `teardown` (a teardown/schematic
  you are allowed to use), `unknown`.
* Store facts and a link, not copied text/images.

## Where to add your own models
Drop extra files in `%LOCALAPPDATA%\SoftBox\PhoneLab\device-components\` – they are loaded next to the bundled ones
and override a bundled file with the same `profileId`.

## Real sources to fill it from
| Source | What it gives | Licence note |
|---|---|---|
| Manufacturer pages (Samsung, Xiaomi, Google ...) | official specs, model numbers | official |
| SoC makers (MediaTek, Qualcomm, Samsung LSI, Unisoc) | SoC cores / GPU / process | official |
| GSMArena, PhoneArena, Kimovil, PhoneMore | specs, bands, battery | facts only, no bulk copy |
| Wikipedia / Wikidata | specs | CC BY-SA / CC0 |
| LineageOS & postmarketOS wikis | SoC, RAM, storage, battery, display, camera | CC BY-SA |
| iFixit teardowns | IC part numbers per device | CC BY-NC-SA: link, do not bundle |
| FCC ID filings (fccid.io) | internal photos, RF info | public records |
| Licensed schematics / boardview (ZXW, WuXinJi, JCID ...) | exact IC positions | licence required |
