using PipelineAlerts.Sources.Anr;
using PipelineAlerts.Models;
using PipelineAlerts.Rules;

// to run, use `dotnet run --project src/PipelineAlerts`
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var source = new AnrNoticeSource(http);

Console.WriteLine($"Checking {source.Name} pipeline...");

try
{
    var notices = await source.FetchAsync(CancellationToken.None);
    var evaluator = new SignalEvaluator();

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
        Console.WriteLine("Slack message sent.");
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