using PipelineAlerts.Models;
using PipelineAlerts.Sources.Anr;

namespace PipelineAlerts.Tests.Sources;

public class AnrXmlParserTests
{
    private static readonly Uri ListLink = new("https://ebb.tceconnects.com/infopost/ReportViewer.aspx?/InfoPost/Notices&assetNbr=3005");

    private static IReadOnlyList<Notice> ParseFixture(string name) =>
        AnrXmlParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)), ListLink);

    [Theory]
    [InlineData("anr_critical.xml", 55)]
    [InlineData("anr_pso.xml", 45)]
    public void Parses_every_notice(string fixture, int expected)
    {
        Assert.Equal(expected, ParseFixture(fixture).Count);
    }

    [Fact]
    public void Maps_initiated_notice_fields_and_converts_central_time_to_utc()
    {
        var n = ParseFixture("anr_critical.xml").Single(x => x.NoticeId == "26306044");

        Assert.Equal("ANR Pipeline Company", n.Pipeline);
        Assert.Equal("Planned Service Outage", n.Type);
        Assert.Equal("CAPACITY REDUCTION Southwest Mainline Northbound", n.Subject);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 19, 31, 59, TimeSpan.Zero), n.PostedUtc); // CDT, UTC-5
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero), n.EndUtc);      // CST, UTC-6
        Assert.Equal(TimeSpan.Zero, n.PostedUtc.Offset);
        Assert.True(n.IsCritical);
        Assert.Equal("Initiate", n.Status);
        Assert.Null(n.PriorNoticeId);
    }

    [Fact]
    public void Reads_status_and_prior_notice_for_superseding_notice()
    {
        var n = ParseFixture("anr_critical.xml").Single(x => x.NoticeId == "26307008");

        Assert.Equal("Supersede", n.Status);
        Assert.Equal("26305023", n.PriorNoticeId);
    }

    [Fact]
    public void Extracts_location_from_body()
    {
        var n = ParseFixture("anr_critical.xml").Single(x => x.NoticeId == "26252033");

        Assert.NotNull(n.Location);
        Assert.Contains("Jefferson Parish, Louisiana", n.Location);
    }

    [Fact]
    public void Non_critical_notice_is_not_critical()
    {
        var n = ParseFixture("anr_pso.xml").Single(x =>
            x.Subject == "CAPACITY REDUCTION Southwest Area" && x.PostedUtc.Date == new DateTime(2026, 10, 12));

        Assert.False(n.IsCritical);
    }

    [Theory]
    [InlineData("anr_critical.xml")]
    [InlineData("anr_pso.xml")]
    public void Every_notice_links_to_the_list(string fixture)
    {
        Assert.All(ParseFixture(fixture), n => Assert.Equal(ListLink, n.Link));
    }

    [Fact]
    public void Malformed_xml_throws()
    {
        Assert.ThrowsAny<Exception>(() => AnrXmlParser.Parse("<Report xmlns=\"Notices\"><Detail", ListLink));
    }

    [Fact]
    public void Detail_without_notice_id_throws_naming_the_field()
    {
        const string xml = """
            <Report xmlns="Notices">
              <Detail NoticeType="Planned Service Outage" Subject="Test" PostedDt="10/14/2026 8:40:34 AM" CriticalInd="Y">
                <Item TSPName="ANR Pipeline Company" NoticeType="Notice Stat Desc: Initiate&#xD;&#xA;Prior Notice: " NoticeText="x" />
              </Detail>
            </Report>
            """;

        var ex = Assert.Throws<FormatException>(() => AnrXmlParser.Parse(xml, ListLink));
        Assert.Contains("NoticeId", ex.Message);
    }
}
