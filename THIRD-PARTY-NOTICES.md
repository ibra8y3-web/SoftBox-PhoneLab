# Third-party notices

PhoneLab integrates or downloads the following third-party software/data. Downloaded tools are stored under the current Windows user's LocalAppData and are not embedded in PhoneLab's own executable.

## TRACE Boardviewer 1.3.3

- Project: https://github.com/trace-boardviewer/trace-boardviewer
- License: MIT (see the upstream LICENSE file).
- Download: https://github.com/trace-boardviewer/trace-boardviewer/releases/download/v1.3.3/TRACE-Boardviewer-1.3.3.exe
- SHA-256 checked by PhoneLab before launch: 88a3bace7a3e71cb1e8136561acd4fbb86d8dd8568da6b179bdc46024c8aacd8
- PhoneLab launches the viewer as a separate process and passes the selected local board-file path. TRACE is not bundled into or linked into PhoneLab.
- No boardview/repair data is included by this integration. The user must possess authorized files.

## scrcpy 5.0.1

- Project: https://github.com/Genymobile/scrcpy
- License: Apache-2.0.
- Windows archive SHA-256 is verified by ScreenMirrorService before extraction.
- Used only for live screen display and input mirroring through the authorized USB/ADB connection.

## Android model-code community data

- Project: https://github.com/bsthen/device-models
- License: Apache-2.0 (upstream repository).
- Downloaded at runtime as a model-code-to-brand/model identity mapping; community records are not treated as certified hardware revision or PCB data.

## iFixit repair search

- PhoneLab uses the public suggestion API to find original repair-guide pages and opens the original URLs.
- It does not mirror guides, photos, diagrams, or copyrighted repair instructions into this repository.

## AndroidX

- AndroidX Core KTX 1.15.0 is declared by the Android Agent and distributed under its upstream AndroidX/Apache-2.0 terms.
 
## QRCoder 1.6.0

- Project: https://github.com/codebude/QRCoder
- NuGet package: https://www.nuget.org/packages/QRCoder/1.6.0
- License: MIT.
- Used locally to encode a repair-ticket identifier as a PNG QR code. QR payloads do not include customer names or phone numbers.
