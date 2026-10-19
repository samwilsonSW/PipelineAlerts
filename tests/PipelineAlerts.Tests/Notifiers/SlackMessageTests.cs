using PipelineAlerts.Notifiers;
using Xunit.Abstractions;

namespace PipelineAlerts.Tests.Notifiers;

/// <summary>
/// Tests for the Slack message text, the one place that decides what a trading-signal alert says.
/// Both the real Slack notifier and the dry-run console notifier use it, so these checks cover both.
/// </summary>
public class SlackMessageTests
{
    private readonly ITestOutputHelper _output;

    public SlackMessageTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Checks that the message carries everything a trader needs at a glance: the notice title, type,
    /// affected pipeline, volume, notice id, the reasons it matched, and a clickable "View notice" link
    /// to the notice page.
    /// </summary>
    [Fact]
    public void Message_contains_the_notice_details_and_link()
    {
        var message = SlackMessage.Build(TestSignals.Make());
        _output.WriteLine(message);

        Assert.Contains("CAPACITY REDUCTION - Southeast Area", message);
        Assert.Contains("Type: Planned Service Outage", message);
        Assert.Contains("Pipeline: ANR Pipeline Company", message);
        Assert.Contains("Volume: 111 MMcf/d", message);
        Assert.Contains("26252033", message);
        Assert.Contains($"<{TestSignals.NoticeLink}|View notice>", message);
        Assert.Contains("Why: recent: posted 2026-10-14; keyword: capacity reduction; relevant: mentions Louisiana", message);
    }

    /// <summary>
    /// Checks that a notice with no volume in its text says "not stated" rather than leaving a blank or
    /// printing "null". Volume is optional per CONTRACT.md, so this case is normal.
    /// </summary>
    [Fact]
    public void Missing_volume_says_not_stated()
    {
        var message = SlackMessage.Build(TestSignals.Make(volume: null));

        Assert.Contains("Volume: not stated", message);
    }

    /// <summary>
    /// Checks that &amp;, &lt; and &gt; in notice text are escaped, because Slack reads them as markup
    /// (a stray "&lt;" could swallow part of the message or make a fake link). The link markup the message
    /// adds itself must stay unescaped so the link still works.
    /// </summary>
    [Fact]
    public void Notice_text_is_escaped_but_link_markup_is_not()
    {
        var message = SlackMessage.Build(TestSignals.Make(subject: "Outage at A&B <Station> 5", type: "Type <x>"));

        Assert.Contains("Outage at A&amp;B &lt;Station&gt; 5", message);
        Assert.Contains("Type: Type &lt;x&gt;", message);
        Assert.DoesNotContain("<Station>", message);
        Assert.Contains($"<{TestSignals.NoticeLink}|View notice>", message);
    }
}
