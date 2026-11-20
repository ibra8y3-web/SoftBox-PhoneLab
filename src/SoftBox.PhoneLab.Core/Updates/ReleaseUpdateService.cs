using System.Net;
using System.Net.Http;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace SoftBox.PhoneLab.Core.Updates;

public enum UpdateChannel
{
    Stable,
    Beta
}

public sealed record ReleaseAssetInfo(string Name, long SizeBytes, string DownloadUrl);

public sealed record ReleaseInfo(
    string TagName,
    string Name,
    string Body,
    string HtmlUrl,
    bool IsPrerelease,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ReleaseAssetInfo> Assets);

public sealed record ReleaseUpdateResult(
    string CurrentVersion,
    UpdateChannel Channel,
    ReleaseInfo? LatestRelease,
    bool UpdateAvailable,
    DateTimeOffset CheckedAtUtc,
    string Message);

/// <summary>
/// Reads public GitHub Releases metadata for SoftBox. It never downloads, executes or installs binaries.
/// Only validated HTTPS links on github.com are returned for user-initiated navigation.
/// </summary>
public sealed class ReleaseUpdateService
{
    public const string RepositoryOwner = "ibra8y3-web";
    public const string RepositoryName = "SoftBox-PhoneLab";
    public const string ReleasesApiUrl = "https://api.github.com/repos/ibra8y3-web/SoftBox-PhoneLab/releases?per_page=30";
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly Regex SemanticVersionPattern = new(
        @"^[vV]?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly HttpClient _http;

    public ReleaseUpdateService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SoftBox-UpdateChecker/1.0");
        if (!_http.DefaultRequestHeaders.Accept.Any())
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<ReleaseUpdateResult> CheckAsync(
        string currentVersion,
        UpdateChannel channel,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseVersion(currentVersion, out var current))
            throw new ArgumentException("رقم الإصدار الحالي غير صالح للمقارنة.", nameof(currentVersion));

        using var response = await _http.GetAsync(
            ReleasesApiUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new ReleaseUpdateResult(
                currentVersion, channel, null, false, DateTimeOffset.UtcNow,
                "لم يعثر GitHub على صفحة Releases عامة لهذا المستودع.");
        }
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long declaredLength && declaredLength > MaximumResponseBytes)
            throw new InvalidDataException("بيانات Releases أكبر من الحد المسموح.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;
            if (memory.Length + read > MaximumResponseBytes)
                throw new InvalidDataException("بيانات Releases تجاوزت 2 MiB.");
            memory.Write(buffer, 0, read);
        }

        using var document = JsonDocument.Parse(memory.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("استجابة GitHub Releases ليست قائمة صالحة.");

        var releases = new List<ReleaseInfo>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetBoolean(item, "draft"))
                continue;

            var tag = GetString(item, "tag_name");
            var htmlUrl = GetString(item, "html_url");
            if (string.IsNullOrWhiteSpace(tag) || !TryParseVersion(tag, out _))
                continue;
            if (!IsOfficialReleaseUrl(htmlUrl))
                continue;

            var prerelease = GetBoolean(item, "prerelease");
            DateTimeOffset? published = DateTimeOffset.TryParse(GetString(item, "published_at"), out var timestamp)
                ? timestamp
                : null;
            var assets = new List<ReleaseAssetInfo>();
            if (item.TryGetProperty("assets", out var assetArray) && assetArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetArray.EnumerateArray().Take(100))
                {
                    var name = GetString(asset, "name");
                    var url = GetString(asset, "browser_download_url");
                    var size = GetInt64(asset, "size");
                    if (!string.IsNullOrWhiteSpace(name) && IsOfficialAssetUrl(url) && size >= 0)
                        assets.Add(new ReleaseAssetInfo(name, size, url));
                }
            }

            releases.Add(new ReleaseInfo(
                tag,
                GetString(item, "name") ?? tag,
                GetString(item, "body") ?? "",
                htmlUrl!,
                prerelease,
                published,
                assets));
        }

        var candidates = releases
            .Where(release => channel == UpdateChannel.Beta || !release.IsPrerelease)
            .OrderByDescending(release => ParseVersion(release.TagName), SemVersionComparer.Instance)
            .ThenByDescending(release => release.PublishedAt)
            .ToArray();

        var latest = candidates.FirstOrDefault();
        if (latest is null)
        {
            return new ReleaseUpdateResult(
                currentVersion, channel, null, false, DateTimeOffset.UtcNow,
                channel == UpdateChannel.Stable
                    ? "لا توجد إصدارات Stable منشورة في صفحة Releases."
                    : "لا توجد إصدارات منشورة في صفحة Releases.");
        }

        var available = Compare(ParseVersion(latest.TagName), current) > 0;
        return new ReleaseUpdateResult(
            currentVersion,
            channel,
            latest,
            available,
            DateTimeOffset.UtcNow,
            available
                ? $"يتوفر إصدار أحدث: {latest.TagName}."
                : $"الإصدار الحالي ({currentVersion}) يساوي أو أحدث من أحدث إصدار في القناة المحددة.");
    }

    public static bool IsOfficialReleaseUrl(string? value)
        => IsOfficialGithubUrl(value) &&
           Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           uri.AbsolutePath.StartsWith($"/{RepositoryOwner}/{RepositoryName}/releases/", StringComparison.OrdinalIgnoreCase);

    public static bool IsOfficialAssetUrl(string? value)
        => IsOfficialGithubUrl(value) &&
           Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           uri.AbsolutePath.StartsWith($"/{RepositoryOwner}/{RepositoryName}/releases/download/", StringComparison.OrdinalIgnoreCase);

    private static bool IsOfficialGithubUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
           uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
           uri.Port == 443 &&
           string.IsNullOrEmpty(uri.UserInfo);

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBoolean(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) &&
           value.ValueKind is JsonValueKind.True;

    private static long GetInt64(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result
            : 0;

    private static bool TryParseVersion(string value, out SemVersion parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100)
            return false;

        var match = SemanticVersionPattern.Match(value.Trim());
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            return false;

        var prerelease = match.Groups[4].Success ? match.Groups[4].Value : "";
        if (prerelease.Split('.').Any(part =>
                part.Length > 1 &&
                part.All(char.IsAsciiDigit) &&
                part.StartsWith('0')))
            return false;

        parsed = new SemVersion(major, minor, patch, prerelease);
        return true;
    }

    private static SemVersion ParseVersion(string value)
    {
        if (!TryParseVersion(value, out var version))
            throw new InvalidDataException("Invalid semantic version: " + value);
        return version;
    }

    private static int Compare(SemVersion left, SemVersion right)
        => SemVersionComparer.Instance.Compare(left, right);

    private readonly record struct SemVersion(int Major, int Minor, int Patch, string Prerelease);

    private sealed class SemVersionComparer : IComparer<SemVersion>
    {
        public static SemVersionComparer Instance { get; } = new();

        public int Compare(SemVersion x, SemVersion y)
        {
            var result = x.Major.CompareTo(y.Major);
            if (result != 0) return result;
            result = x.Minor.CompareTo(y.Minor);
            if (result != 0) return result;
            result = x.Patch.CompareTo(y.Patch);
            if (result != 0) return result;

            if (x.Prerelease.Length == 0 || y.Prerelease.Length == 0)
            {
                if (x.Prerelease.Length == y.Prerelease.Length) return 0;
                return x.Prerelease.Length == 0 ? 1 : -1;
            }

            var left = x.Prerelease.Split('.');
            var right = y.Prerelease.Split('.');
            for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
            {
                var leftNumeric = int.TryParse(left[index], out var leftNumber);
                var rightNumeric = int.TryParse(right[index], out var rightNumber);
                if (leftNumeric && rightNumeric)
                    result = leftNumber.CompareTo(rightNumber);
                else if (leftNumeric != rightNumeric)
                    result = leftNumeric ? -1 : 1;
                else
                    result = string.Compare(left[index], right[index], StringComparison.Ordinal);
                if (result != 0) return result;
            }
            return left.Length.CompareTo(right.Length);
        }
    }
}
