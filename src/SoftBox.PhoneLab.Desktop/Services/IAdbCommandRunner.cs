namespace SoftBox.PhoneLab.Desktop.Services;

/// <summary>
/// Narrow command boundary used by ADB-backed services.
/// Keeping the process runner behind an interface makes protocol orchestration testable
/// without pretending that a test process is a connected Android device.
/// </summary>
public interface IAdbCommandRunner
{
    bool IsAvailable { get; }

    Task<string> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken,
        TimeSpan? commandTimeout = null);
}
