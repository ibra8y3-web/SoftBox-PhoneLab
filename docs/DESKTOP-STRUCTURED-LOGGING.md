# Windows desktop structured logging and crash capture

SoftBox desktop attaches handlers to the WPF Dispatcher, AppDomain and TaskScheduler unobserved-task event stream. Each event is stored as one JSON object per line in:

`%LOCALAPPDATA%/SoftBox/PhoneLab/Logs/desktop-events.jsonl`

The log contains UTC timestamp, source, exception type, bounded message/stack text, process ID and termination metadata. The file is rotated when it grows beyond 1 MiB and is kept in the current Windows user's LocalAppData folder. No telemetry is uploaded automatically. Exception text and stack traces may contain local paths or device context; inspect the file before sharing it. The telemetry handler does not mark WPF dispatcher failures as handled and does not conceal the application shutdown path.

Android Agent logs are separate: bounded JSONL events and crash captures stay in app-private storage, and the technician may export them explicitly from the Windows Agent test panel.
