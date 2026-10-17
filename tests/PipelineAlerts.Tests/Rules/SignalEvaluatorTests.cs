using PipelineAlerts.Models;
using PipelineAlerts.Rules;
using PipelineAlerts.Sources.Anr;
using Xunit.Abstractions;

namespace PipelineAlerts.Tests.Rules;

/// <summary>
/// Tests for the signal evaluator, the piece that decides whether one notice is a trading signal
/// (recent + signal keyword + Louisiana / Henry Hub relevance, per CONTRACT.md) and explains why.
/// Most tests use small hand-built notices so each rule can be checked on its own; the last test runs
/// the real saved ANR reports through it. No network, and the clock is pinned so results never drift.
/// </summary>
public class SignalEvaluatorTests
{
    // Pinned "now" for every test, so "recent" never depends on when the tests run.
    private static readonly DateTimeOffset NowUtc = new(2026, 10, 15, 17, 0, 0, TimeSpan.Zero);

    private readonly ITestOutputHelper _output;

    public SignalEvaluatorTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Test helper: builds a notice with sensible defaults (posted one day before NowUtc, not critical,
    /// empty body). Each test sets only the fields it cares about, so the point of the test stays visible.
    /// </summary>
    private static Notice MakeNotice(
        string subject = "General notice",
        string body = "",
        string type = "Informational",
        DateTimeOffset? postedUtc = null) => new()
    {
        Pipeline = "ANR Pipeline Company",
        NoticeId = "1",
        Type = type,
        Status = "Initiate",
        Subject = subject,
        Body = body,
        PostedUtc = postedUtc ?? NowUtc.AddDays(-1),
        IsCritical = false,
        Link = new Uri("https://example.com/notice/1"),
    };

    private static SignalResult Evaluate(Notice notice) => new SignalEvaluator().Evaluate(notice, NowUtc);

    /// <summary>
    /// Checks that a notice posted 10 days ago is not a signal even though it has a keyword and mentions
    /// Louisiana. Old news is already priced in, so the Recent rule must be able to veto on its own.
    /// </summary>
    [Fact]
    public void Old_notice_is_not_a_signal()
    {
        var result = Evaluate(MakeNotice("Force Majeure", "Compressor in Louisiana", postedUtc: NowUtc.AddDays(-10)));

        Assert.False(result.IsSignal);
        Assert.StartsWith("skipped: posted 2026-10-05", result.Reasons[0]);
    }

    /// <summary>
    /// Checks that a notice posted exactly at the cutoff (now minus 3 days) still counts as recent.
    /// The boundary is "within the last 3 days" inclusive; this pins it so an off-by-one can't creep in.
    /// </summary>
    [Fact]
    public void Posted_exactly_at_cutoff_counts_as_recent()
    {
        var result = Evaluate(MakeNotice("Outage", "Louisiana", postedUtc: NowUtc.AddDays(-3)));

        Assert.StartsWith("recent:", result.Reasons[0]);
        Assert.True(result.IsSignal);
    }

    /// <summary>
    /// Checks that "Non-Force Majeure" on its own is not a keyword match. It is tariff wording that appears
    /// in routine notices; counting it would flag nearly everything as a force majeure event.
    /// </summary>
    [Fact]
    public void Non_force_majeure_alone_is_not_a_keyword()
    {
        var result = Evaluate(MakeNotice("Maintenance", "This is a Non-Force Majeure event in Louisiana."));

        Assert.False(result.IsSignal);
        Assert.Equal("skipped: no signal keyword", result.Reasons[1]);
    }

    /// <summary>
    /// Checks that a real "force majeure" still matches when "Non-Force Majeure" also appears in the text.
    /// Removing the tariff wording must not hide a genuine force majeure declaration next to it.
    /// </summary>
    [Fact]
    public void Real_force_majeure_matches_alongside_non_force_majeure()
    {
        var result = Evaluate(MakeNotice("Update", "Non-Force Majeure terms apply. ANR declares force majeure in Louisiana."));

        Assert.Equal("keyword: force majeure", result.Reasons[1]);
        Assert.True(result.IsSignal);
    }

    /// <summary>
    /// Checks the main happy path: a recent "CAPACITY REDUCTION" subject with a Louisiana location in the body
    /// is a signal, with three passing reasons. This is the shape of the real ANR notices the app exists to catch.
    /// </summary>
    [Fact]
    public void Capacity_reduction_in_subject_with_louisiana_is_a_signal()
    {
        var result = Evaluate(MakeNotice("CAPACITY REDUCTION Southeast Mainline", "Work located in St. Mary Parish, Louisiana."));

        Assert.True(result.IsSignal);
        Assert.Equal(["recent: posted 2026-10-14", "keyword: capacity reduction", "relevant: mentions Louisiana"], result.Reasons);
    }

    /// <summary>
    /// Checks that "capacity reduction" in the body only does not count. CONTRACT.md limits that phrase to the
    /// subject, because bodies use it loosely in routine text.
    /// </summary>
    [Fact]
    public void Capacity_reduction_in_body_only_is_not_a_keyword()
    {
        var result = Evaluate(MakeNotice("Maintenance", "There may be a capacity reduction in Louisiana."));

        Assert.False(result.IsSignal);
        Assert.Equal("skipped: no signal keyword", result.Reasons[1]);
    }

    /// <summary>
    /// Checks that the Type field is ignored: Type "Planned Service Outage" with no keyword in subject or body
    /// is not a keyword match. Every notice on the outage list has that Type, so using it would flag them all.
    /// </summary>
    [Fact]
    public void Type_field_does_not_count_as_keyword()
    {
        var result = Evaluate(MakeNotice("Meter maintenance", "Work in Louisiana.", type: "Planned Service Outage"));

        Assert.False(result.IsSignal);
        Assert.Equal("skipped: no signal keyword", result.Reasons[1]);
    }

    /// <summary>
    /// Checks that naming a Henry Hub-linked pipeline (here "columbia gulf", in lowercase) is enough to be relevant,
    /// with no mention of Louisiana. Interconnect notices often name the other pipeline, not the state.
    /// </summary>
    [Fact]
    public void Henry_hub_pipeline_name_makes_notice_relevant()
    {
        var result = Evaluate(MakeNotice("Outage", "Reduced flow at the columbia gulf interconnect."));

        Assert.Equal("relevant: mentions Columbia Gulf", result.Reasons[2]);
        Assert.True(result.IsSignal);
    }

    /// <summary>
    /// Checks that a notice with no Louisiana, Henry Hub or listed pipeline mention is not a signal, and that
    /// the reason says so. Most ANR notices are in the Midwest and don't move Henry Hub prices.
    /// </summary>
    [Fact]
    public void Notice_without_relevant_mention_is_not_a_signal()
    {
        var result = Evaluate(MakeNotice("Outage", "Compressor work in Defiance County, Ohio."));

        Assert.False(result.IsSignal);
        Assert.Equal("skipped: no Louisiana / Henry Hub mention", result.Reasons[2]);
    }

    /// <summary>
    /// Checks that a notice failing all three rules still gets exactly three reasons, each a "skipped" one.
    /// CONTRACT.md says Reasons always explains the decision, so no rule may be skipped silently.
    /// </summary>
    [Fact]
    public void Every_rule_adds_a_reason_even_when_all_fail()
    {
        var result = Evaluate(MakeNotice("Meeting", "Customer meeting in Detroit.", postedUtc: NowUtc.AddDays(-30)));

        Assert.False(result.IsSignal);
        Assert.Equal(3, result.Reasons.Count);
        Assert.All(result.Reasons, reason => Assert.StartsWith("skipped:", reason));
    }

    /// <summary>
    /// Checks that a volume is returned exactly as written, in any of the three units, and null when there is none.
    /// Traders want the number as the pipeline posted it, so there is no unit conversion.
    /// </summary>
    [Theory]
    [InlineData("Capacity reduced by 111 MMcf/d.", "111 MMcf/d")]
    [InlineData("Capacity of 5,000 Dth available.", "5,000 Dth")]
    [InlineData("Up to 12.5 MMBtu per hour.", "12.5 MMBtu")]
    [InlineData("No volume given.", null)]
    public void Volume_is_kept_as_written(string body, string? expected)
    {
        Assert.Equal(expected, Evaluate(MakeNotice("Update", body)).Volume);
    }

    /// <summary>
    /// Checks that a lookback of zero or fewer days is rejected. CONTRACT.md says the setting must be more than 0;
    /// a zero window would quietly turn the alert off.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Lookback_must_be_positive(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SignalEvaluator(days));
    }

    /// <summary>
    /// Checks that a custom lookback widens the Recent window: a 10-day-old notice fails with the default
    /// 3 days but passes with 30. This proves the setting is actually used, not just stored.
    /// </summary>
    [Fact]
    public void Custom_lookback_widens_the_window()
    {
        var notice = MakeNotice("Outage", "Louisiana", postedUtc: NowUtc.AddDays(-10));

        Assert.False(new SignalEvaluator().Evaluate(notice, NowUtc).IsSignal);
        Assert.True(new SignalEvaluator(30).Evaluate(notice, NowUtc).IsSignal);
    }

    /// <summary>
    /// Runs the real saved ANR reports (Critical + Planned Service Outage, merged by NoticeId like the source does)
    /// through the evaluator. Every result must have three reasons, and the set of signals is pinned to what was
    /// checked by hand against the subjects and bodies (none, for this data). If the rules or the parser change, this shows it.
    /// </summary>
    [Fact]
    public void Real_anr_notices_give_the_expected_signals()
    {
        var merged = new List<Notice>();
        var seen = new HashSet<string>();
        foreach (var fixture in new[] { "anr_critical.xml", "anr_pso.xml" })
        {
            var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
            foreach (var notice in AnrXmlParser.Parse(xml, "3005"))
            {
                if (seen.Add(notice.NoticeId))
                    merged.Add(notice);
            }
        }

        var evaluator = new SignalEvaluator();
        var signalIds = new List<string>();
        int recent = 0, keyword = 0, relevant = 0;
        foreach (var notice in merged)
        {
            var result = evaluator.Evaluate(notice, NowUtc);
            Assert.Equal(3, result.Reasons.Count);
            if (result.Reasons[0].StartsWith("recent:")) recent++;
            if (result.Reasons[1].StartsWith("keyword:")) keyword++;
            if (result.Reasons[2].StartsWith("relevant:")) relevant++;
            _output.WriteLine($"{notice.NoticeId} signal={result.IsSignal} volume={result.Volume} | {string.Join(" | ", result.Reasons)} || {notice.Subject}");
            if (result.IsSignal)
                signalIds.Add(notice.NoticeId);
        }

        _output.WriteLine($"{merged.Count} notices: {recent} recent, {keyword} keyword, {relevant} relevant, {signalIds.Count} signals");

        // Checked by hand: the 6 recent notices are all Midwest / Southwest work (Missouri, Kansas, Illinois,
        // Texas / Oklahoma) or the maintenance schedule. The Louisiana notices (26252033, 26143017) and the
        // NGPL one (26250013) are all older than 3 days. So this saved data has no signals.
        Assert.Empty(signalIds);
    }
}
