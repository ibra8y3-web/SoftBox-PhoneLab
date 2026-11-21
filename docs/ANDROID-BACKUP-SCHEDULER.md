# Android encrypted backup scheduler

## How it runs

- The scheduler checks authorized Android ADB devices every 30 seconds while an Administrator or Technician session is open.
- It supports three schedule kinds: on device connection, daily at a local 24-hour time, and weekly on a selected local weekday/time.
- The schedule engine runs independently of the visible device-list refresh setting.
- It launches encrypted shared-storage backup jobs asynchronously so that device discovery and the WPF interface remain responsive.
- A schedule can be run immediately, paused, re-enabled, refreshed, or deleted without deleting backup files already created.

## Local protection

- Schedule definitions and backup passwords are saved in a DPAPI CurrentUser-protected file under the current Windows user profile.
- The password is never written as plaintext schedule JSON.
- Output archives use SoftBox's authenticated SBX encrypted container and per-file manifest verification.
- DPAPI schedule settings are bound to the same Windows user profile. Moving the schedule store to another Windows account or computer does not make it portable.

## Requirements and limits

- SoftBox must remain open and the user must remain signed in for scheduled tasks to run.
- The phone must be connected and ADB-authorized at the scheduled time.
- This scheduler backs up only Android shared-storage directories that SoftBox is allowed to read. It does not copy private application data, system partitions, or protected directories.
- It does not create Windows Task Scheduler jobs, wake a sleeping/shut-down PC, or run after SoftBox exits.
- On-connect schedules are triggered by the next scheduler poll after the selected device is detected and authorized.
