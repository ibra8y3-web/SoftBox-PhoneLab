# Board-map and component-data contribution guide

This directory accepts source-backed, model-specific hardware information only. Do not add guessed component positions, inferred chip part numbers, generic copied images or a board map labelled "verified" without repeatable evidence.

## Required identity

Every board map must identify:
- Manufacturer and exact commercial model (including regional/model suffix where relevant).
- Board revision / PCB code printed on the actual board.
- Device codename only as a separate alias; it does not replace the commercial model.
- Image/board dimensions and coordinate units. The coordinate origin is top-left unless the source explicitly states otherwise.
- A source record for each factual component or board layer. Record the source publisher, public reference or licensed source name, date obtained and allowed-use note.

## Component records

Each component needs a stable reference designator or unique ID, category, visible label/part number where actually read, x/y coordinate in the declared unit, confidence and one or more source IDs. If the part number is hidden or unreadable, set confidence to unknown and leave the part number empty. Do not infer a PMIC, charging IC, RF IC or memory part number from the device brand alone.

## Review status

- draft: imported or transcribed, not independently checked.
- source-reviewed: the source and model/board revision were checked.
- verified: two independent source/evidence paths or licensed boardview plus physical cross-check; reviewer and review date required.
- needs-review: conflict or model/board-revision mismatch remains.

A phone model profile is not a PCB schematic. Board-view files, electrical nets, test points and measurements must not be populated from generic Android data.

## Existing inventory baseline

- data/boardmaps currently contains the generic Android map only.
- data/device-components contains a Galaxy A31 detailed record and its rules README.
- data/device-profiles/catalog.json contains 12 curated device profiles.
- The separate community catalogue is not counted as source-verified PCB coverage.

The accompanying board-map schema describes the required structure for future submissions. It does not retroactively certify existing generic data. Only submit a map when you have the actual model/board revision and the right to redistribute the source-derived facts.
