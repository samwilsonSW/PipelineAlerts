using PipelineAlerts.Notifiers;

namespace PipelineAlerts.Tests.Notifiers;

/// <summary>
/// Tests for the dry-run notifier, used when no SLACK_WEBHOOK_URL is set: it prints the Slack message
/// instead of sending it.
/// </summary>
public class ConsoleNotifierTests
{
    /// <summary>
    /// Checks that a dry run writes a "[dry run]" header and the exact Slack message to the given writer.
    /// The notifier has no HttpClient at all, so it cannot make a network call.
    /// </summary>
    [Fact]
    public async Task Prints_dry_run_header_and_the_message()
    {
        var writer = new StringWriter();
        var signal = TestSignals.Make();

        await new ConsoleNotifier(writer).SendAsync(signal, CancellationToken.None);

        var printed = writer.ToString();
        Assert.StartsWith("[dry run] Slack message:", printed);
        Assert.Contains(SlackMessage.Build(signal), printed);
    }
}
