using System.Net;
using PipelineAlerts.Models;
using PipelineAlerts.Sources.Anr;

namespace PipelineAlerts.Tests.Sources;

/// <summary>
/// Tests for the ANR source, the piece that downloads ANR's two notice reports and merges them into one list.
/// No real network calls: a fake HTTP handler answers each request with a saved report (a "fixture" file),
/// so the tests are fast, repeatable, and check against what ANR actually publishes.
/// </summary>
public class AnrNoticeSourceTests
{
    private const string Base = "https://ebb.tceconnects.com/infopost/ReportViewer.aspx?/InfoPost/Notices";
    private const string CriticalUrl = Base + "&assetNbr=3005&CritFlag=1&rs:Format=XML";
    private const string OutageUrl = Base + "&assetNbr=3005&CritFlag=-1&NoticeType=25&rs:Format=XML";
    private const string NoticePage = "https://ebb.tceconnects.com/infopost/ReportViewer.aspx?/InfoPost/NoticesSubreport&pNoticeId=";

    /// <summary>
    /// Test helper: a fake HTTP handler. It serves a fixture file for each known URL, or a chosen error status,
    /// and records every URL it was asked for. Anything unexpected gets a 404 so a wrong URL shows up as a failure.
    /// </summary>
    private sealed class FakeHandler(HttpStatusCode outageStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];
        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.AbsoluteUri; // the URL as actually sent
            lock (RequestedUrls)
            {
                RequestedUrls.Add(url);
                UserAgents.Add(request.Headers.UserAgent.ToString());
            }

            var (status, fixture) = url switch
            {
                CriticalUrl => (HttpStatusCode.OK, "anr_critical.xml"),
                OutageUrl => (outageStatus, "anr_pso.xml"),
                _ => (HttpStatusCode.NotFound, null),
            };
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.OK && fixture != null)
                response.Content = new StringContent(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)));
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Test helper: runs the source once against the fake handler and returns the merged notices.
    /// </summary>
    private static async Task<IReadOnlyList<Notice>> FetchWith(FakeHandler handler) =>
        await new AnrNoticeSource(new HttpClient(handler)).FetchAsync(CancellationToken.None);

    /// <summary>
    /// Checks that merging the Critical list (55) and the Planned Service Outage list (45) gives 57 notices,
    /// each NoticeId once. The lists overlap heavily; without merging by NoticeId the same notice would be
    /// alerted twice. CONTRACT.md: "Merge by NoticeId so each notice appears once."
    /// </summary>
    [Fact]
    public async Task Merges_both_lists_into_unique_notices()
    {
        var notices = await FetchWith(new FakeHandler());

        Assert.Equal(57, notices.Count);
        Assert.Equal(57, notices.Select(n => n.NoticeId).Distinct().Count());
    }

    /// <summary>
    /// Checks that notice 26306044, which is on both lists, appears once and links to its own notice page
    /// (with AssetNbr=3005). Critical comes first so its copy is kept, but the link is the same from either list.
    /// </summary>
    [Fact]
    public async Task Notice_on_both_lists_keeps_critical_copy()
    {
        var notices = await FetchWith(new FakeHandler());

        var n = Assert.Single(notices, x => x.NoticeId == "26306044");
        Assert.Equal(NoticePage + "26306044&AssetNbr=3005", n.Link.OriginalString);
    }

    /// <summary>
    /// Checks that a notice found only on the outage list ("CAPACITY REDUCTION Southwest Area", posted 10/12/2026)
    /// is kept, links to its own notice page (with AssetNbr=3005), and is not critical. This proves the outage list
    /// adds notices the Critical list doesn't have, rather than being thrown away during the merge.
    /// </summary>
    [Fact]
    public async Task Outage_only_notice_is_kept_with_own_link()
    {
        var notices = await FetchWith(new FakeHandler());

        var n = Assert.Single(notices, x =>
            x.Subject == "CAPACITY REDUCTION Southwest Area" && x.PostedUtc.Date == new DateTime(2026, 10, 12));
        Assert.Equal(NoticePage + n.NoticeId + "&AssetNbr=3005", n.Link.OriginalString);
        Assert.False(n.IsCritical);
    }

    /// <summary>
    /// Checks that exactly the two report URLs from CONTRACT.md are requested, each once, with the
    /// "PipelineAlerts/1.0" User-Agent. A wrong URL would quietly fetch the wrong report (or none).
    /// </summary>
    [Fact]
    public async Task Requests_exactly_the_two_contract_urls()
    {
        var handler = new FakeHandler();
        await FetchWith(handler);

        Assert.Equal(new[] { CriticalUrl, OutageUrl }.Order(), handler.RequestedUrls.Order());
        Assert.All(handler.UserAgents, ua => Assert.Equal("PipelineAlerts/1.0", ua));
    }

    /// <summary>
    /// Checks that if the outage list returns HTTP 500, the whole fetch fails with a message naming the outage
    /// URL and the status code, instead of returning only the Critical notices. CONTRACT.md: never report a
    /// partial or empty result for a failed fetch.
    /// </summary>
    [Fact]
    public async Task Server_error_on_either_list_throws_with_url()
    {
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => FetchWith(new FakeHandler(HttpStatusCode.InternalServerError)));

        Assert.Contains(OutageUrl, ex.Message);
        Assert.Contains("500", ex.Message);
    }

    /// <summary>
    /// Checks the source's name is "ANR", which is how it is identified in console output.
    /// </summary>
    [Fact]
    public void Name_is_ANR()
    {
        Assert.Equal("ANR", new AnrNoticeSource(new HttpClient()).Name);
    }
}
