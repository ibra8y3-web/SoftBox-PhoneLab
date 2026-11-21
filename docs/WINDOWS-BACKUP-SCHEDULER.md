# Persistent Android backup scheduler

Android backup schedules and passwords remain protected in the existing current-user DPAPI store. The UI can install a Windows Task Scheduler job named \\SoftBoxPhoneLabAndroidBackupWorker. It starts the same published SoftBox executable every five minutes with --run-android-backups; this background-only path skips the interactive SoftBox sign-in window and reads only the pre-existing DPAPI-protected schedules under the same Windows user.

The worker starts the bundled/available ADB client, discovers currently authorized devices and processes due schedules. The scheduler uses a cross-process lock plus a persisted last-attempt cooldown to prevent the visible app timer and the Task Scheduler worker from reserving the same due job concurrently. It waits for the due backup jobs and stores result state in the protected schedule database.

The task is configured with the current Windows identity and interactive-only logon (/IT). It continues after the SoftBox window is closed, but it does not run while the user is signed out, the PC is powered off/sleeping, the USB cable is absent, or ADB authorization is revoked. This is a Windows local scheduled task, not a cloud service. It backs up only the configured shared-storage folders, to encrypted SBX files; it does not copy app-private or system partitions.
