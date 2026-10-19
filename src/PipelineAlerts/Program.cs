using PipelineAlerts.Sources.Anr;
using PipelineAlerts.Models;
using PipelineAlerts.Rules;
using PipelineAlerts.Interfaces;
using PipelineAlerts.Notifiers;

// to run, use `dotnet run --project src/PipelineAlerts`
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var source = new AnrNoticeSource(http);

/// Lookback period for recent notices, in days. 
const int lookbackDays = 3;

// No SLACK_WEBHOOK_URL means dry run: print the Slack message instead of sending it.
// The webhook URL is a secret; never print it.
var webhookUrl = Environment.GetEnvironmentVariable("SLACK_WEBHOOK_URL");
var isDryRun = string.IsNullOrWhiteSpace(webhookUrl);
INotifier notifier;
if (isDryRun)
{
    notifier = new ConsoleNotifier();
}
else if (Uri.TryCreate(webhookUrl!.Trim(), UriKind.Absolute, out var webhookUri)
         && (webhookUri.Scheme == Uri.UriSchemeHttps || webhookUri.Scheme == Uri.UriSchemeHttp))
{
    notifier = new SlackNotifier(http, webhookUri);
}
else
{
    Console.Error.WriteLine("SLACK_WEBHOOK_URL is not a valid URL.");
    return 1;
}

Console.WriteLine($"Checking {source.Name} pipeline...");

try
{
    var notices = await source.FetchAsync(CancellationToken.None);
    var evaluator = new SignalEvaluator(lookbackDays);

    Console.WriteLine($"Found {notices.Count} notices.");

    var nowUtc = DateTimeOffset.UtcNow;

    var signals = new List<SignalResult>();
    foreach (var notice in notices)
    {
        var verdict = evaluator.Evaluate(notice, nowUtc);

        if (verdict.IsSignal) 
            signals.Add(verdict);
    }

    Console.WriteLine($"{signals.Count} trading signal(s) detected.");

    // foreach (var result in signals)
    // {
    //     var tag = result.IsSignal ? "[SIGNAL]" : "[skip]  ";
    //     Console.WriteLine($"  {tag} {result.Notice.NoticeId}  {result.Notice.Subject}  ({string.Join("; ", result.Reasons)})");
    // }

    if (signals.Count > 0)
    {
        try
        {
            foreach (var signal in signals)
            {
                await notifier.SendAsync(signal, CancellationToken.None);
                Console.WriteLine(isDryRun ? "Dry run: Slack message printed." : "Slack message sent.");
            }
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Slack send failed: {ex.Message}");
            return 1;
        }
    }
    else
    {
        Console.WriteLine("No slack message sent (no trading signals).");
    }

    
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Fetch failed: {ex.Message}");
    return 1;
}

return 0;