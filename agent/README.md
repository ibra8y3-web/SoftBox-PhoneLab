# PhoneLab Agent

Android companion app for SoftBox PhoneLab Windows.

## Build
Open this folder in Android Studio or install JDK 17 and Gradle 8.11.1, then run:

    gradle :app:assembleDebug

APK output:

    app/build/outputs/apk/debug/app-debug.apk

Install on a phone you own or are authorized to diagnose:

    adb install -r app/build/outputs/apk/debug/app-debug.apk

Open the app and leave it in the foreground while using Windows PhoneLab.

## USB bridge
The app starts a small HTTP endpoint bound to device loopback 127.0.0.1:27831 while the Activity is resumed. Windows creates:

    adb -s DEVICE_SERIAL forward tcp:27831 tcp:27831

The Windows app reads /snapshot and can POST an allow-listed test command to /command. The endpoint is not bound to Wi-Fi/LAN and has no remote telemetry or arbitrary shell execution.

## Permissions
Microphone permission is requested only when the microphone test is started. Camera testing hands off to the installed camera app rather than silently recording. Vibration uses the Android vibrator service.

## Test interpretation
The Agent records test status and short evidence notes. Some tests are user-confirmed and should be interpreted as screening evidence rather than calibrated measurements.

## Remote Windows test control (Agent 0.4.0)

The Agent binds only to 127.0.0.1:27831. Windows accesses it through `adb forward`; it is not exposed on Wi-Fi/LAN. The desktop can send an allow-listed POST command to start display, touch, speaker, earpiece, microphone, flashlight, vibration, camera/AF, sensor sampling, hardware inventory and battery observation. Permission prompts and user confirmations stay visible on the phone. The Agent does not accept arbitrary shell commands or firmware-writing operations.
