# Importing model-specific board maps

PhoneLab supports model-specific component-placement maps in its own JSON schema. The import action is available after the phone is detected and inspected.

## Import checks

A map is accepted only when:
- IsVerifiedForModel is true.
- AccuracyLevel is pcb, boardview or manufacturer.
- The map lists an exact model number or device codename read from the connected phone.
- If the map specifies Manufacturer, it matches the connected phone.
- If the map specifies HardwareRevision or BoardRevision, PhoneLab can read the same revision from the phone.
- A source HTTPS URL, source name, reviewer and verification timestamp are present.
- Part IDs are unique and all coordinates/dimensions are finite and within accepted limits.

The importer checks the file's metadata and structural validity. It cannot independently prove that a source document is authentic, licensed, or that the submitted coordinates were transcribed correctly. The technician remains responsible for verifying the original source and revision.

## JSON map shape

    {
      "id": "samsung-a515f-board-rev-1",
      "displayName": "Galaxy A51 SM-A515F board layout",
      "manufacturer": "Samsung",
      "modelNumbers": ["SM-A515F"],
      "deviceCodes": ["a51"],
      "hardwareRevision": "REV1.0",
      "boardRevision": "REV1",
      "sourceName": "Licensed boardview source / manufacturer service document",
      "sourceUri": "https://example.invalid/document-reference",
      "sourceDocumentId": "document-reference",
      "verifiedBy": "Technician / reviewer",
      "verifiedAtUtc": "2026-11-19T00:00:00Z",
      "accuracyLevel": "boardview",
      "isVerifiedForModel": true,
      "parts": [
        {
          "id": "mainboard",
          "name": "Main board",
          "x": 0.0,
          "y": 0.0,
          "z": 0.8,
          "width": 5.5,
          "height": 8.4,
          "depth": 0.2,
          "baseColor": "#64748B"
        }
      ]
    }

Replace the example URI and all example values with the actual source and measured or transcribed data. The example.invalid value above is deliberately non-resolving.

## Current geometry scope

The current WPF 3D renderer draws verified component boxes using their X/Y/Z positions and dimensions. It is a component-placement view, not yet a full multi-layer boardview renderer: it does not draw copper traces, pads, vias, pins, electrical nets, board stackups or full chassis mechanics.

Native OpenBoardView formats are not all decoded directly by PhoneLab yet. The upstream ecosystem supports multiple distinct formats, including BRD/BRD2, BDV, BVR, GenCAD and others; their structures and encodings differ. Use legitimate tools and a licensed source to derive component positions, then import a reviewed PhoneLab JSON bundle. Never convert an unrelated board file by simply renaming its extension.

## Revision matching

PhoneLab reads common Android properties when the vendor exposes them, including ro.boot.hardware.revision, ro.boot.hwrev, ro.boot.revision, ro.boot.board.revision and ro.boot.board_version. Many devices hide revision identifiers. A map that requires a revision will be rejected if the phone does not report the exact required value.

## Native BoardView files

PhoneLab can launch the separate MIT-licensed TRACE Boardviewer 1.3.3 Windows portable viewer for supported original board files. It downloads the viewer on first use, verifies the published SHA-256, then passes the local board file as a process argument. It does not upload the board file. TRACE supports selected variants of BRD/BRD2/BVR and other documented formats; unsupported variants remain unsupported. This native viewer is separate from the PhoneLab JSON placement-map importer and does not create model-specific board coordinates when source files are absent.
