# External PhoneLab measurement rig protocol

PhoneLab accepts passive read-only serial measurement devices.

- Port: selectable Windows COM port
- Default speed: 115200 baud, 8-N-1
- Request, one JSON line: {"command":"sample","schema":1}

Response, one JSON line per request:

    {
      "schema": 1,
      "instrument": "BenchRig-01",
      "usbVoltageV": 5.08,
      "batteryVoltageV": 3.91,
      "batteryCurrentA": -0.42,
      "boardTemperatureC": 31.8,
      "usbPowerW": 2.15,
      "rails": { "VBUS": 5.08, "VBAT": 3.91 }
    }

All fields except schema and instrument are optional. Values must be finite JSON numbers. Missing values remain unavailable; PhoneLab does not invent measurements.

The Windows client sends only the sample read command. It does not issue power-on, power-off, voltage setpoint, current limit, discharge, flashing or board-control commands. The measurement fixture itself must be electrically safe, isolated where needed and rated for the circuit under test.

## Limits
A USB-C/PD meter can measure the USB input contract, voltage and current in the USB path. It cannot see internal PMIC rails or individual component pins by itself. Access to VBAT, VPH_PWR, VBUS, buck rails and connector pins requires a model-specific test adapter and safe measurement procedure.
