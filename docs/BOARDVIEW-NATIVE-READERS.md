# Native BoardView and PCB formats

## What SoftBox does now

SoftBox probes the first 64 KiB of a selected board file for conservative format signatures (TOPTEST BRD/BRD2, Landrex/TestLink BRD, Honhan BDV, BVRAW/BVR, GenCAD, KiCad PCB, Altium PcbDoc, EAGLE XML and eM-Test ASC). This is format identification only; a filename extension is not proof that the content is valid.

The original file is opened locally by the bundled, SHA-256-verified TRACE Boardviewer 1.3.3 parser. The viewer receives the original path; SoftBox does not upload the board file. Upstream parser information:

- https://github.com/trace-boardviewer/trace-boardviewer
- https://github.com/trace-boardviewer/trace-boardviewer/releases/tag/v1.3.3

## Internal 3D map import boundary

TRACE is a separate program. Its successful opening of a BRD/BDV/BVR file does **not** mean SoftBox has decoded all component coordinates, pin/pad positions, traces, nets and layers into its own BoardMap model.

The SoftBox 3D board scene currently imports reviewed SoftBox BoardMap JSON files and normalized, technician-reviewed CSV containing references, positions, dimensions, sides, rotations, layers, footprints, values and net metadata.

Those imports remain bound to the exact manufacturer/model/device code and confirmed physical board revision. Source URL, document identifier, declared license, reviewer and SHA-256 metadata are required. Unknown geometry is never generated from a model name.

## Still incomplete

1. A native in-process reader extracting components, pads, pins, traces, layers and net connectivity from every original BoardView variant into the internal 3D scene.
2. A legally distributable, technician-verified map for every phone model and PCB revision.
3. Hardware-in-the-loop verification that each extracted map matches the physical board and exact revision markings.

Do not represent the external viewer handoff or signature probe as completion of these items. Native formats can contain vendor-specific binary layouts and revision differences; parser adapters need valid sample files and tests for each variant.
