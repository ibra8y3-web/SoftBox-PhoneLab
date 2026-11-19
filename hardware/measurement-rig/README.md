# Optional PhoneLab bench instrument

This is an interface definition, not a ready-made phone motherboard probe. A safe rig needs a properly rated USB-C/PD meter and/or isolated voltage/current probes with a technician-designed adapter for the target board.

The serial firmware may report USB input voltage/current, battery-path voltage/current, and a temperature probe only where the probes have been safely wired and calibrated. It must not power, short, or inject voltage into a phone board by default.

See docs/MEASUREMENT-RIG-PROTOCOL.md for the read-only JSON-lines contract. The firmware or instrument must answer sample requests with schema 1 JSON records. Do not connect a bare MCU ADC to phone VBAT or USB-C VBUS; use rated divider/current-sense circuitry, proper isolation and over-voltage protection.
