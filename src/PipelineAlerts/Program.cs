using PipelineAlerts.Sources.Anr;

// to run, use `dotnet run --project src/PipelineAlerts`
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var source = new AnrNoticeSource(http);

Console.WriteLine($"Checking {source.Name} pipeline...");

try
{
    var notices = await source.FetchAsync(CancellationToken.None);
    var nowUtc = DateTimeOffset.UtcNow;

// Logic for filtering based on conditions in 2.
    static bool Has(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);

    string[] hhPipelines = ["Columbia Gulf", "Gulf South", "NGPL", "Natural Gas Pipeline", "Sea Robin",
                            "Southern Natural", "Texas Gas", "Transco", "Transcontinental", "Trunkline",
                            "Acadian", "Bridgeline", "Jefferson Island", "Sabine"];

    var recent = notices.Where(n =>
    {
        var text = n.Subject + " " + n.Body;
        var cleaned = text.Replace("Non-Force Majeure", "", StringComparison.OrdinalIgnoreCase);

        bool isRecent   = n.PostedUtc >= nowUtc.AddDays(-3);
        bool hasKeyword = Has(cleaned, "force majeure") || Has(text, "outage")
                    || Has(text, "curtailment") || Has(n.Subject, "capacity reduction");
        bool isRelevant = Has(text, "Louisiana") || hhPipelines.Any(p => Has(text, p));

        return isRecent && hasKeyword && isRelevant;
    }).ToList();

    Console.WriteLine($"{recent.Count} of {notices.Count} are recent or still in effect.");

    Console.WriteLine($"Found {recent.Count} recent or relevant notices.");

    foreach (var n in recent.OrderByDescending(n => n.PostedUtc).Take(10))
        Console.WriteLine($"  {n.PostedUtc:yyyy-MM-dd HH:mm}Z  {n.NoticeId}  {n.Type,-24}  {n.Subject} \n {n.Link} \n");
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Fetch failed: {ex.Message}");
    return 1;
}

return 0;