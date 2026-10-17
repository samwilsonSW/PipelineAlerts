using PipelineAlerts.Models;
using PipelineAlerts.Sources.Anr;

namespace PipelineAlerts.Tests.Sources;

/// <summary>
/// Tests for the ANR parser, the piece that turns ANR's downloaded XML notice report into Notice records.
/// Almost every test reads a real report saved from the ANR site (a "fixture" file), so the parser is
/// checked against what ANR actually publishes, with no network calls.
/// </summary>
public class AnrXmlParserTests
{
    private const string AssetNbr = "3005";

    /// <summary>
    /// Test helper: loads a saved ANR report from the Fixtures folder and runs it through the parser
    /// with ANR's asset number (3005), which the parser puts into each notice's link.
    /// Keeping this in one place means every test reads its fixture the same way.
    /// </summary>
    private static IReadOnlyList<Notice> ParseFixture(string name) =>
        AnrXmlParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)), AssetNbr);

    /// <summary>
    /// Checks that the parser returns every notice in each saved report: 55 in the Critical list and 45 in
    /// the Planned Service Outage list. This catches notices being silently dropped, for example a query that
    /// misses the XML's "Notices" namespace and finds nothing. CONTRACT.md says fail loudly, never skip rows.
    /// </summary>
    [Theory]
    [InlineData("anr_critical.xml", 55)]
    [InlineData("anr_pso.xml", 45)]
    public void Parses_every_notice(string fixture, int expected)
    {
        Assert.Equal(expected, ParseFixture(fixture).Count);
    }

    /// <summary>
    /// Checks one real notice (26306044) field by field: pipeline, type, subject, dates, critical flag, status,
    /// and that a brand-new ("Initiate") notice has no prior notice. The count tests only prove nothing is lost;
    /// this proves the values are right. It was posted in October (daylight time, UTC-5) and ends in late November
    /// (standard time, UTC-6), so it also catches Central-to-UTC conversion bugs on both sides of the daylight-saving change.
    /// </summary>
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

    /// <summary>
    /// Checks notice 26307008, which replaces an earlier one: its status must read "Supersede" and its prior notice
    /// must be 26305023. ANR packs both values as separate lines inside one attribute, so this catches mistakes in
    /// splitting those lines apart. Later code needs the link to know which older notice is now out of date.
    /// </summary>
    [Fact]
    public void Reads_status_and_prior_notice_for_superseding_notice()
    {
        var n = ParseFixture("anr_critical.xml").Single(x => x.NoticeId == "26307008");

        Assert.Equal("Supersede", n.Status);
        Assert.Equal("26305023", n.PriorNoticeId);
    }

    /// <summary>
    /// Checks that notice 26252033's location is pulled from its body text and includes "Jefferson Parish, Louisiana".
    /// Location drives the Louisiana / Henry Hub relevance rule, so if extraction breaks, real signals would be missed.
    /// The location only comes from the text itself; CONTRACT.md says it is never guessed.
    /// </summary>
    [Fact]
    public void Extracts_location_from_body()
    {
        var n = ParseFixture("anr_critical.xml").Single(x => x.NoticeId == "26252033");

        Assert.NotNull(n.Location);
        Assert.Contains("Jefferson Parish, Louisiana", n.Location);
    }

    /// <summary>
    /// Checks that a notice ANR marks as not critical (the "CAPACITY REDUCTION Southwest Area" notice posted
    /// 2026-10-12 in the planned outage list) comes out with the critical flag off. The other tests only see critical
    /// notices, so without this a parser that always said "critical" would still pass.
    /// </summary>
    [Fact]
    public void Non_critical_notice_is_not_critical()
    {
        var n = ParseFixture("anr_pso.xml").Single(x =>
            x.Subject == "CAPACITY REDUCTION Southwest Area" && x.PostedUtc.Date == new DateTime(2026, 10, 12));

        Assert.False(n.IsCritical);
    }

    /// <summary>
    /// Checks that every notice in both saved reports links to its own page on the ANR site: the notice page
    /// address with that notice's own NoticeId and AssetNbr=3005. This catches notices sharing one link, or
    /// a link that is missing the asset number (without it the site labels the notice as the wrong pipeline).
    /// </summary>
    [Theory]
    [InlineData("anr_critical.xml")]
    [InlineData("anr_pso.xml")]
    public void Every_notice_links_to_its_own_page(string fixture)
    {
        Assert.All(ParseFixture(fixture), n => Assert.Equal(
            $"https://ebb.tceconnects.com/infopost/ReportViewer.aspx?/InfoPost/NoticesSubreport&pNoticeId={n.NoticeId}&AssetNbr=3005",
            n.Link.OriginalString));
    }

    /// <summary>
    /// Checks notice 26306044's link against the exact address, written out in full. The address was opened in a
    /// browser and shows this notice as ANR, so this pins the link to one known-good value rather than a pattern.
    /// </summary>
    [Fact]
    public void Known_notice_link_is_exact()
    {
        var n = ParseFixture("anr_critical.xml").Single(x => x.NoticeId == "26306044");

        Assert.Equal(
            "https://ebb.tceconnects.com/infopost/ReportViewer.aspx?/InfoPost/NoticesSubreport&pNoticeId=26306044&AssetNbr=3005",
            n.Link.AbsoluteUri);
    }

    /// <summary>
    /// Checks that a broken, cut-off XML document makes the parser throw an error instead of returning an empty list.
    /// An empty list would look like "no notices today" when the download actually failed.
    /// CONTRACT.md says to fail loudly and never report zero notices for a bad response.
    /// </summary>
    [Fact]
    public void Malformed_xml_throws()
    {
        Assert.ThrowsAny<Exception>(() => AnrXmlParser.Parse("<Report xmlns=\"Notices\"><Detail", AssetNbr));
    }

    /// <summary>
    /// Checks that a notice with no NoticeId makes the parser throw, and that the error message names the missing field.
    /// Without an ID a notice can't be merged across the two lists or matched to the notice it replaces.
    /// This enforces CONTRACT.md's "missing field: fail loudly, don't skip rows quietly", with a message that says what to fix.
    /// </summary>
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

        var ex = Assert.Throws<FormatException>(() => AnrXmlParser.Parse(xml, AssetNbr));
        Assert.Contains("NoticeId", ex.Message);
    }
}
