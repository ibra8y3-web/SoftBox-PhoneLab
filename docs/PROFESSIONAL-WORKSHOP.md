# Professional workshop workspace

## Local data
The workshop workspace stores its JSON database at:

`%LocalAppData%\\SoftBox\\PhoneLab\\Workshop\\workshop.json`

No cloud account or network service is used for customer, repair-order or inventory records. The database is encrypted using Windows DPAPI in the current Windows user's scope, with purpose separation from the activity/notification database. Writes use a protected temporary file and atomic replacement. Legacy plaintext workshop JSON schemas v1-v5 are validated and upgraded to schema v5 plus DPAPI protection on first successful read; failed decryption is reported and does not reset the data.

## Repair workflow
- Creating a repair order records the customer's name and optional phone, device model, optional serial/IMEI note, symptoms reported at intake and estimated cost.
- A unique ticket reference is generated locally. The optional QR PNG contains only a `softbox-phonelab://repair/<ticket>` identifier; it does not embed customer PII and is not a remote tracking URL.
- A technician can record findings before repair, work performed, tests/results after repair, status and final cost.
- The lifecycle statuses are: received, diagnosis, waiting for parts, in repair, ready for pickup, delivered and cancelled.
- The exported technician report contains the entered facts and a limitation statement. It is not a guarantee or independent certification.

## Inventory and audit history
Inventory is keyed by SKU and records item name, on-hand quantity, low-stock threshold, estimated unit cost and storage location. Stock changes require a non-zero signed quantity and a reason. The application rejects a decrement that would make stock negative. The latest 5,000 business events are kept in the local file.

## Backups and privacy
Use the backup tab to export a DPAPI-protected .sbxbackup file or restore one after an explicit confirmation. The backup uses Windows CurrentUser protection and can be restored only in the same Windows user context; it is not a portable cross-account/machine backup. Legacy plaintext JSON backups remain importable and are re-saved in protected form. Exported CSV/HTML/JSON reports are separate files and are not automatically encrypted; protect them before sharing. Do not store a full IMEI or other highly sensitive identifier unless the repair workflow truly requires it.

## Current scope
This is a local, single-workstation workflow. It is not yet a LAN multi-user server, employee time-clock/payroll module, supplier purchasing system, accounting package or cloud synchronization service. Stock adjustments are manual; parts are not automatically deducted from an order unless the technician records the corresponding stock movement.
