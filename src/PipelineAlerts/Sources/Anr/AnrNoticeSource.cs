using PipelineAlerts.Interfaces;
using PipelineAlerts.Models;

namespace PipelineAlerts.Sources.Anr;

/// <summary>Downloads ANR's Critical and Planned Service Outage notice lists and merges them into one list.</summary>
public sealed class AnrNoticeSource : INoticeSource
{
    private const string BaseUrl = "https://ebb.tceconnects.com/infopost/ReportViewer.aspx?/InfoPost/Notices";
    private const string XmlFormat = "&rs:Format=XML";
    private const string AssetNbr = "3005"; // ANR's pipeline number on the site

    // The two reports, as machine-readable XML.
    private const string CriticalUrl = BaseUrl + "&assetNbr=" + AssetNbr + "&CritFlag=1" + XmlFormat;
    private const string OutageUrl = BaseUrl + "&assetNbr=" + AssetNbr + "&CritFlag=-1&NoticeType=25" + XmlFormat;

    private readonly HttpClient _http;

    /// <param name="http">The caller owns its lifetime and timeout.</param>
    public AnrNoticeSource(HttpClient http) => _http = http;

    public string Name => "ANR";

    public async Task<IReadOnlyList<Notice>> FetchAsync(CancellationToken ct)
    {
        // Each report takes several seconds to generate, so request both at once.
        var critical = FetchListAsync(CriticalUrl, ct);
        var outage = FetchListAsync(OutageUrl, ct);
        await Task.WhenAll(critical, outage);

        // The lists overlap. Critical comes first, so a notice on both keeps the Critical copy (the link is the same either way).
        var merged = new List<Notice>();
        var seen = new HashSet<string>();
        foreach (var notice in (await critical).Concat(await outage))
        {
            if (seen.Add(notice.NoticeId))
                merged.Add(notice);
        }
        return merged;
    }

    private async Task<IReadOnlyList<Notice>> FetchListAsync(string url, CancellationToken ct)
    {
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
            return AnrXmlParser.Parse(xml, AssetNbr);
        }
    }
}
