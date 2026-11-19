# Screen and camera tests

## Display pattern sequence
The Android Agent provides a full-screen sequence of 10 patterns: red, green, blue, white, black, 25% gray, 50% gray, 75% gray, cyan and magenta. The user advances each pattern and confirms the screen only after inspecting all of them. A user-reported defect is recorded as failure; cancelling remains not-tested.

## Camera-photo pixel screening
PhoneLab can analyze a camera photograph of the physical phone display. Before analysis the technician selects which uniform pattern (white, black, red, green, blue, gray or unknown) was shown. The analyzer compares each sampled pixel against a local 3×3 neighborhood and reports separate dark, bright and color-outlier candidates plus luminance uniformity.

This is deliberately a candidate detector, not an automatic certified dead-pixel count. Reflections, moire, focus, perspective, exposure, camera sensor artifacts and glass contamination can generate false results. Capture the panel nearly perpendicular, use a stable exposure/focus, turn off flash and fill the image with the active display area.

## Camera2 inventory and autofocus
The Android Agent enumerates logical camera IDs and exposed physical camera routes. For each accessible route it:
- Records facing direction, focal lengths, sensor pixel dimensions, flash flag and advertised autofocus modes.
- Attempts a JPEG capture through Camera2.
- Reads the reported AF state for autofocus-capable routes and records it separately from image capture.
- Records fixed-focus/no-AF routes as data-read, not failures.

A Camera2 route may map to several user-facing lenses or modes. Vendor firmware may hide routes or allow only a logical-camera path. AF state is not equivalent to chart sharpness; OIS/EIS, macro quality, dust, scratches and optical alignment require a physical target and inspection.

## Other phone-side tests
The Windows Remote Test panel can dispatch display, touch, speaker, earpiece, microphone, flashlight, vibration, camera, live sensors, hardware inventory and battery observation tests. Confirmation and permission prompts stay on the phone. The phone Agent must be foregrounded and connected through USB/ADB.
