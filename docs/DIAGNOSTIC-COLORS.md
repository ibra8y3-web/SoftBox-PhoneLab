# Diagnostic color semantics

| Color | State | Meaning |
|---|---|---|
| Green | Pass | A named functional test completed and produced positive evidence. |
| Yellow | Review | A reported condition or ambiguous test needs technician review. |
| Red | Failed | A named test reported failure. |
| Blue | DataRead | A system identifier or observation was read; this is not a health verdict. |
| Gray | NotTested | No test evidence exists for this component. |

The state machine never turns a component green because the phone merely connected, and it never turns an IC red because a different subsystem showed a symptom. Every state is tied to an evidence note in the exported report.

Examples:
- SoC name read from Android: blue.
- Battery reports overheat: yellow.
- User confirms no audio during the speaker test: red.
- All touchscreen grid cells were touched: green for the named touch-coverage test only.
- PMIC not measured: gray.
