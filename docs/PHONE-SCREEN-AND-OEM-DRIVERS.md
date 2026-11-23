# Phone screen dock and OEM USB driver center

## Live phone screen

The home workspace keeps the Android scrcpy host visible in its own right-side panel beside the 3D assembly. The phone list, component list and navigation remain in separate columns. Selecting an authorized ADB device can start its screen session automatically when the existing preference `AutoOpenScreenMirror` is enabled. The screen panel also provides explicit start/refresh, stop and recording actions.

Before launching scrcpy, PhoneLab rechecks the selected device's ADB state and makes a best-effort `KEYCODE_WAKEUP` request. That can wake a sleeping display; it does not unlock the device or bypass a lock screen. The Android operating system must be booted, USB debugging must be enabled, and the user must approve this computer. A powered-off phone, bootloader-only screen, unsupported OEM transport or an unauthorized ADB device cannot be mirrored through scrcpy.

## Components

The component diagnostic list is initialized when the main window starts, including when no device is connected. Every component remains visible as `Not tested` until an actual read or user-approved test supplies evidence.

## OEM USB driver resources

The driver center lists 20 OEM/Google resources referenced by the Android Developers OEM USB driver directory. These are HTTPS manufacturer support/download pages, not bundled installers. The exact package can depend on the model, region and Windows version. PhoneLab never silently installs an OEM driver, disables Windows signature enforcement, or treats a generic package as compatible with every model.

Google's USB driver is for Google devices; other manufacturers provide their own Windows drivers. For any OEM package selected by the technician, retain the vendor signature and verify the exact model/Windows compatibility before installation.


## CI and permissions
The driver center is a read-only action and is explicitly assigned the DeviceRead permission for every role that has device-read access. The component catalog is populated during initial window load even when ADB is unavailable. Opening an embedded module keeps the connected-device and component columns visible, and the live screen host is layered above the module surface.


Automatic screen retries run independently from the device-inventory change check. If ADB is still authorized but scrcpy has exited, PhoneLab retries the screen session with a 12-second per-device throttle; it does not repeat phone inspection or Agent installation. Direct screen and recording controls enforce the same role permissions as their module actions.
