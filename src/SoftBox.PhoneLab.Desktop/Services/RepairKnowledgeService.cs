using System.Net.Http;
using System.Text.Json;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record RepairKnowledgeLink(string Title, string Url, string Kind, string Summary);

/// <summary>On-demand links to licensed repair knowledge; does not bulk-copy or train on repair content.</summary>
public sealed class RepairKnowledgeService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public async Task<IReadOnlyList<RepairKnowledgeLink>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var url = "https://www.ifixit.com/api/2.0/suggest/" + Uri.EscapeDataString(query.Trim()) + "?doctypes=device,guide";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return [];

        var output = new List<RepairKnowledgeLink>();
        foreach (var item in results.EnumerateArray().Take(10))
        {
            var title = item.TryGetProperty("title", out var titleValue) ? titleValue.GetString() ?? "" : "";
            var link = item.TryGetProperty("url", out var urlValue) ? urlValue.GetString() ?? "" : "";
            var kind = item.TryGetProperty("dataType", out var typeValue) ? typeValue.GetString() ?? "" : "";
            var summary = item.TryGetProperty("summary", out var summaryValue) ? summaryValue.GetString() ?? "" : "";
            if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
                uri.Host.Equals("www.ifixit.com", StringComparison.OrdinalIgnoreCase) && title.Length > 0)
                output.Add(new RepairKnowledgeLink(title, link, kind, summary));
        }
        return output;
    }
}
