using PipelineAlerts.Interfaces;
using PipelineAlerts.Models;

namespace PipelineAlerts.Notifiers;

/// <summary>
/// Dry-run notifier, used when no SLACK_WEBHOOK_URL is set: prints the Slack message instead of sending it.
/// Writes to Console.Out unless another writer is passed in (tests pass a StringWriter).
/// </summary>
public sealed class ConsoleNotifier : INotifier
{
    private readonly TextWriter _output;

    public ConsoleNotifier(TextWriter? output = null) => _output = output ?? Console.Out;

    public Task SendAsync(SignalResult signalResult, CancellationToken ct)
    {
        _output.WriteLine("[dry run] Slack message:");
        _output.WriteLine(SlackMessage.Build(signalResult));
        return Task.CompletedTask;
    }
}
