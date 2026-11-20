# Modular PhoneLab Navigation

## Navigation convention

PhoneLab uses a persistent right-side navigation rail grouped by product area. Each substantial capability lives in its own page or dedicated WPF window rather than being appended to the diagnostics dashboard.

To add a new capability:
1. Create a dedicated WPF window named after the feature (for example, `NewFeatureWindow.cs`).
2. Keep its models and services in their existing project folders; do not place business logic in the navigation control.
3. Add one clearly named button under the matching group in `MainWindow.xaml`.
4. Add a small click handler in `MainWindow.xaml.cs` that opens the feature window with `Owner = this`.
5. Add unit tests for portable business rules and update this document or the feature's own documentation.
6. Run the Windows workflow and confirm the desktop build and tests before calling the section complete.

## Current groups

- Diagnostics and USB connection
- Phone tests and screen tools
- Workshop and global administration
- Device profiles, PCB maps and BoardView
- Measurement instruments
- USB transport, Android logs and firmware inspection

The original workshop window remains available. The Global Workshop window adds international settings, warranty operations, technician timeline, printable HTML reports and CSV exports. Both use the same local workshop database.

## Data and deployment limits

Workshop data stays in the current Windows user's LocalAppData folder. Backups contain customer data and are not encrypted by PhoneLab; protect the Windows account and backup destination. CSV files are for export/integration only; PhoneLab does not perform tax calculation, accounting certification, exchange-rate conversion or cloud synchronization.
