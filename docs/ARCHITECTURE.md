# Architecture

SoftBox PhoneLab has three trust boundaries.

## Windows desktop
- WPF desktop UI and interactive 3D assembly viewer.
- ADB discovery and process execution.
- Read-only system inspection through a fixed command allow-list.
- ADB port forwarding to the on-device Agent.
- Board-map catalog resolution and diagnostic report export.

## Core engine
- Parses Android property, CPU, memory, storage, display and battery observations.
- Normalizes test evidence into NotTested, DataRead, Pass, Review, Failed or Unavailable.
- Keeps a strict distinction between data discovery and a functional test result.
- Stores model-map geometry separately from diagnostic outcomes.

## Android Agent
- Runs user-started tests on the physical phone.
- Provides display color checks, touch coverage, speaker tone, microphone recording, vibration confirmation, camera intent handoff and sensor inventory.
- Hosts a loopback-only HTTP endpoint while the Agent is foregrounded.
- Windows reaches it through an explicit ADB forward. The Agent does not open a LAN server or upload diagnostic results to a remote host.

## Data path
1. Windows polls ADB for connected devices.
2. The user authorizes the PC on the Android device.
3. The inspection engine reads supported system observations.
4. The user opens PhoneLab Agent and runs requested tests.
5. Windows forwards a local TCP port through ADB and reads the Agent JSON snapshot.
6. The diagnostic engine updates only components that have matching evidence.
7. A JSON report records observations, test results, timestamps and limitations.

## 3D board maps
The generic-android.json file is an educational exploded-view fallback. A verified map must have an exact manufacturer/model/variant source, rights to use the drawing, provenance, and review before isVerifiedForModel can be true. A generic map must never be presented as an exact PCB layout.
