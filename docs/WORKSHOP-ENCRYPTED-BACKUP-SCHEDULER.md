# Workshop encrypted backup scheduler

## Function

- Registers a daily Windows Task Scheduler job for the current Windows user.
- Copies the DPAPI-protected workshop database bytes into a timestamped `.sbxbackup` file.
- Does not decrypt, parse, or export customer records during the scheduled task.
- Uses the current user's interactive Windows token and least privilege.
- Starts a missed task when Windows becomes available again and the user is signed in.
- Deletes only old files matching `SoftBox-Workshop-Backup-*.sbxbackup` in the configured destination, using the user-defined retention period.

## Requirements

- The workshop database must have been loaded/migrated to DPAPI-protected storage first.
- The Windows account must be signed in for an InteractiveToken scheduled task to run.
- The workstation must be awake and Task Scheduler available; this task does not wake a sleeping or powered-off PC.
- The schedule is separate from Android phone backup schedules and does not access a connected phone.

## Controls

The dedicated Workshop Manager workspace configures the daily local time, destination, retention days, queries task status and deletes the scheduled task. Deleting the task does not delete historical backups.
