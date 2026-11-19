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
      "batteryCapacityMah": 4180,
      "batteryInternalResistanceMilliOhm": 82.5,
      "rails": { "VBUS": 5.08, "VBAT": 3.91, "VPH_PWR": 3.84 }
    }

All fields except schema and instrument are optional. Values must be finite JSON numbers. Missing values remain unavailable; PhoneLab does not invent measurements.

The Windows client sends only the sample read command. It does not issue power-on, power-off, voltage setpoint, current limit, discharge, flashing or board-control commands. The measurement fixture itself must be electrically safe, isolated where needed and rated for the circuit under test.

## Limits
A USB-C/PD meter can measure the USB input contract, voltage and current in the USB path. It cannot see internal PMIC rails or individual component pins by itself. Access to VBAT, VPH_PWR, VBUS, buck rails and connector pins requires a model-specific test adapter and safe measurement procedure.

## Optional battery analyzer values

A compatible external battery analyzer may also send numeric JSON fields `batteryCapacityMah` and `batteryInternalResistanceMilliOhm`. PhoneLab logs these instrument observations without assigning a pass/fail limit unless the correct battery type, temperature, SOC range, calibration and model-specific service specification are available. The serial bridge is read-only and never sends a voltage/current setpoint to the phone.

The optional `batteryCapacityMah` and `batteryInternalResistanceMilliOhm` fields should only be sent when the instrument itself performs the corresponding controlled test and has been calibrated. PhoneLab treats the result as review evidence, not an automatic pass/fail decision.
