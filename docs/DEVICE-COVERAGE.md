# Device coverage and hardware limits

## Current Android path
The Windows reader uses ADB. The owner must enable USB debugging and approve the RSA trust prompt. Available fields vary by Android version, manufacturer, driver and user profile.

Data that may be read:
- Manufacturer, model, product/device code, board and build identifier fields exposed by Android properties.
- SoC and CPU identification hints, CPU ABI and logical CPU count.
- RAM total from /proc/meminfo.
- User-data partition size/usage from df.
- Screen resolution and density.
- Android battery service level, voltage, temperature, health and charging status.
- Security patch and baseband strings where exposed.

## Agent-assisted tests
- Display color sequence with user confirmation.
- Touch coverage grid.
- Speaker tone with user confirmation.
- Microphone recording amplitude observation.
- Vibration pulse with user confirmation.
- Camera app hand-off and returned thumbnail observation.
- Sensor inventory.

These are screening tests, not calibrated laboratory tests. A user confirmation does not measure frequency response, color accuracy, dead-pixel count, camera MTF, microphone sensitivity or vibration acceleration.

## Board-level diagnostics not supplied by software alone
A desktop app cannot prove solder-joint integrity, shorted rails, PMIC operation under load, charge-controller behavior, UFS/NAND electrical health, connector pin continuity or water damage through ADB. These require model-specific service documentation and external measurement tools such as a current-limited supply, USB-C/PD meter, oscilloscope, thermal camera or board microscope.

## iPhone
This repository does not claim generic iPhone hardware read access over USB. iOS uses separate Apple-controlled diagnostics and pairing interfaces. Any future iPhone support must use authorized interfaces and must label unsupported tests.


## Community device database pipeline

The maintained catalog is assembled from two independent public sources and regenerated weekly:

- Google Play supported-device model-code mapping via [bsthen/device-models](https://github.com/bsthen/device-models) (Apache-2.0): model code, brand and marketing name.
- [LineageOS Wiki device metadata](https://github.com/LineageOS/lineage_wiki/tree/main/_data/devices) (MIT): device codename, model variants, SoC, CPU/GPU, RAM/storage, display, battery capacity, connectivity, peripherals and supported LineageOS branches when published.

The generated `community-catalog.json` records the retrieval time, source digest, record count and license for each source. Community records always remain `identityVerified=false`. If two records disagree or duplicate an identifier, the resolver must retain the ambiguity rather than select an arbitrary hardware identity. These records do not populate `BoardMapId`, board revision, PCB coordinates or repair limits. They describe model identity and published specifications only.

Run locally with:

```powershell
python -m pip install PyYAML==6.0.3
python scripts/sync_device_profiles.py
python scripts/validate_device_profiles.py
```

The scheduled GitHub workflow refreshes the snapshot every Monday and on manual dispatch. Device-profile coverage is broader than the curated starter list, but it is not a guarantee that every device ever manufactured or every regional variant is represented.
