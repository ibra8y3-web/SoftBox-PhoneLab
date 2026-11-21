using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record SoftBoxDesktopTelemetryEntry(
    DateTimeOffset TimestampUtc,
    string Event,
    string Source,
    string ExceptionType,
    string Message,
    string StackTrace,
    bool IsTerminating,
    int ProcessId);

/// <summary>
/// Local structured diagnostics for the Windows desktop process.
/// Logs are bounded, user-profile local, and never uploaded automatically.
/// </summary>
public static class SoftBoxDesktopTelemetry
{
    private const long MaximumLogBytes = 1024 * 1024;
    private const int MaximumRetainedLines = 1200;
    private const int MaximumMessageLength = 2000;
    private const int MaximumStackLength = 16000;
    private static readonly object Gate = new();
    private static int _initialized;
    private static string? _logPath;

    public static string LogPath => _logPath ?? GetDefaultLogPath();

    public static void Initialize(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        _logPath = GetDefaultLogPath();
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var error = args.ExceptionObject as Exception
                ?? new InvalidOperationException("Unhandled non-Exception object.");
            Record("appdomain-unhandled", error, args.IsTerminating);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Record("task-unobserved", args.Exception, isTerminating: false);
            args.SetObserved();
        };
        application.DispatcherUnhandledException += (_, args) =>
        {
            Record("wpf-dispatcher-unhandled", args.Exception, isTerminating: false);
            // Preserve WPF's default shutdown/error behavior; telemetry must not conceal an application failure.
        };

        Record("desktop-started", new InvalidOperationException("SoftBox desktop process started."), isTerminating: false, includeStack: false);
    }

    public static SoftBoxDesktopTelemetryEntry CreateEntry(
        string eventName,
        Exception exception,
        bool isTerminating,
        DateTimeOffset? timestampUtc = null,
        bool includeStack = true)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new SoftBoxDesktopTelemetryEntry(
            (timestampUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            Normalize(eventName, 80),
            "SoftBox.PhoneLab.Desktop",
            Normalize(exception.GetType().FullName ?? exception.GetType().Name, 240),
            Normalize(exception.Message, MaximumMessageLength),
            includeStack ? Normalize(exception.StackTrace ?? string.Empty, MaximumStackLength) : string.Empty,
            isTerminating,
            Environment.ProcessId);
    }

    public static void Record(
        string eventName,
        Exception exception,
        bool isTerminating,
        bool includeStack = true)
    {
        try
        {
            var entry = CreateEntry(eventName, exception, isTerminating, includeStack: includeStack);
            Append(entry);
        }
        catch
        {
            // Diagnostics must never create a second crash or interrupt shutdown.
        }
    }

    private static void Append(SoftBoxDesktopTelemetryEntry entry)
    {
        var path = LogPath;
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > MaximumLogBytes)
            {
                var retained = File.ReadLines(path).TakeLast(MaximumRetainedLines).ToArray();
                File.WriteAllLines(path + ".tmp", retained);
                File.Move(path + ".tmp", path, overwrite: true);
            }

            var json = JsonSerializer.Serialize(entry);
            File.AppendAllText(path, json + Environment.NewLine);
        }
    }

    private static string GetDefaultLogPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "Logs", "desktop-events.jsonl");

    private static string Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var safe = new string(value.Where(character => !char.IsControl(character) || character is '\r' or '\n' or '\t').ToArray());
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }
}
