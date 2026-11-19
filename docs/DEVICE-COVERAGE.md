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
