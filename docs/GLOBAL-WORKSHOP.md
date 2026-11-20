# Global workshop operations

## Center profile
The Global Workshop workspace stores the center's business name, country, address, phone, email, optional tax-registration reference, currency code, culture/locale, operating time-zone identifier, report language and default warranty duration.

Currency must be a three-letter ISO 4217 code such as EGP, USD, EUR or GBP. PhoneLab labels and formats values in that configured currency; it does not convert currencies or retrieve exchange rates. No tax rate is inferred: country-specific VAT/GST/sales-tax rules and e-invoice certification are not built into this release.

Culture/locale controls numeric formatting in documents. Time zone is stored as the workstation-supported time-zone identifier; raw database timestamps remain UTC to avoid ambiguous storage during daylight-saving changes.

## Warranty lifecycle
Each repair order stores a warranty duration in months. When its status changes to تم التسليم, PhoneLab records the delivery time and calculates an expiry date using calendar-month arithmetic. Changing the duration of an already-delivered order recalculates the expiry from the original delivery timestamp. Zero months means no workshop warranty is recorded.

This field records the workshop's entered policy only. It is not legal advice and does not remove statutory consumer rights, manufacturer coverage or mandatory local warranty terms. The application does not auto-approve or deny customer claims.

## Technician timeline
A repair order can hold dated technician notes. Notes are retained in the local workshop database and included in the print-friendly HTML report. Record observed measurements and actions, not guessed conclusions. The event history records the order reference and note entry.

## Search and operational indicators
The page filters orders by ticket number, customer, phone, device model, reported symptoms and status. Dashboard counts show orders not yet delivered/cancelled, delivered orders, current workshop warranties and low-stock items.

## Print and data exchange
- HTML reports support Arabic RTL and English LTR output and can be printed or saved as PDF through the installed browser.
- Repair-order CSV and inventory CSV are UTF-8 with a BOM for common spreadsheet compatibility.
- CSV values identify their currency code. Imports should preserve the currency column.
- Customer and repair records remain local. Files exported by a technician can contain personal or device-identifying information; handle them according to applicable privacy and consumer-protection rules.

## Database migration
Schema v1 is upgraded to v2 when the existing JSON database is loaded. Existing customers, repair orders, inventory and events are retained; new settings and per-order timeline/warranty fields receive defaults. Keep an external backup before moving data between application versions.

## Current limitations
The current database is for one Windows workstation and is not a synchronized multi-branch database. This release does not include employee authentication/permissions, encrypted-at-rest database files, accounting ledgers, supplier purchasing, automatic stock deduction on repair consumption, payment gateway integration, exchange-rate conversion, tax computation, legally certified e-invoicing, customer SMS/WhatsApp gateways or cloud customer portals.
