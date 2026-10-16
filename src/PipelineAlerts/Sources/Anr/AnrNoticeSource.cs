using PipelineAlerts.Interfaces;
using PipelineAlerts.Models;

namespace PipelineAlerts.Sources.Anr;

/// <summary>Downloads ANR's Critical and Planned Service Outage notice lists and merges them into one list.</summary>
public sealed class AnrNoticeSource : INoticeSource
{
    private const string BaseUrl = "https://ebb.tceconnects.com/infopost/ReportViewer.aspx?/InfoPost/Notices";
    private const string XmlFormat = "&rs:Format=XML";

    // The page a person can open in a browser; adding XmlFormat gives the machine-readable report.
    private static readonly Uri CriticalPage = new(BaseUrl + "&assetNbr=3005&CritFlag=1");
    private static readonly Uri OutagePage = new(BaseUrl + "&assetNbr=3005&CritFlag=-1&NoticeType=25");

    private readonly HttpClient _http;

    /// <param name="http">The caller owns its lifetime and timeout.</param>
    public AnrNoticeSource(HttpClient http) => _http = http;

    public string Name => "ANR";

    public async Task<IReadOnlyList<Notice>> FetchAsync(CancellationToken ct)
    {
        // Each report takes several seconds to generate, so request both at once.
        var critical = FetchListAsync(CriticalPage, ct);
        var outage = FetchListAsync(OutagePage, ct);
        await Task.WhenAll(critical, outage);

        // The lists overlap. Critical comes first, so a notice on both keeps the Critical copy and link.
        var merged = new List<Notice>();
        var seen = new HashSet<string>();
        foreach (var notice in (await critical).Concat(await outage))
        {
            if (seen.Add(notice.NoticeId))
                merged.Add(notice);
        }
        return merged;
    }

    private async Task<IReadOnlyList<Notice>> FetchListAsync(Uri page, CancellationToken ct)
    {
        var url = page.OriginalString + XmlFormat;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("PipelineAlerts/1.0");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // Network failure, or the HttpClient timeout (which surfaces as TaskCanceledException).
            throw new HttpRequestException($"ANR fetch failed for {url}: {ex.Message}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"ANR fetch failed for {url}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);

            var xml = await response.Content.ReadAsStringAsync(ct);
            return AnrXmlParser.Parse(xml, page);
        }
    }
}
