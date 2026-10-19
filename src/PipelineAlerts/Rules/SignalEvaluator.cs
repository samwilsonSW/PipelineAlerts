using System.Globalization;
using System.Text.RegularExpressions;
using PipelineAlerts.Interfaces;
using PipelineAlerts.Models;

namespace PipelineAlerts.Rules;

/// <summary>
/// Applies the v1 signal rules from CONTRACT.md to one notice: Recent, Keyword and Relevant.
/// Pure: no network, no files, no clock. The current time is passed in.
/// </summary>
public sealed class SignalEvaluator : ISignalEvaluator
{
    // Words that mean capacity is going away. "capacity reduction" is checked separately, in the subject only.
    private static readonly string[] Keywords = ["force majeure", "outage", "curtailment"];

    // Pipelines linked to Henry Hub, from Wikipedia's "Henry Hub" article.
    // A starting list, not a complete one.
    private static readonly string[] HenryHubPipelines =
    [
        "Acadian", "Columbia Gulf", "Gulf South", "Bridgeline", "NGPL", "Natural Gas Pipeline", "Sea Robin",
        "Southern Natural", "Texas Gas", "Transco", "Transcontinental", "Trunkline", "Jefferson Island", "Sabine",
    ];

    // A number (digits, optional commas / decimal) then a volume unit, e.g. "111 MMcf/d", "5,000 Dth", "12.5 MMBtu".
    private static readonly Regex VolumePattern = new(
        @"\d[\d,]*(\.\d+)?\s?(MMcf/d|Dth|MMBtu)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly int _lookbackDays;

    public SignalEvaluator(int lookbackDays = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lookbackDays);
        _lookbackDays = lookbackDays;
    }

    public SignalResult Evaluate(Notice notice, DateTimeOffset nowUtc)
    {
        var text = notice.Subject + " " + notice.Body;
        var reasons = new List<string>();

        // Rule 1, Recent: judged by the posted date only.
        var posted = notice.PostedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var isRecent = notice.PostedUtc >= nowUtc.AddDays(-_lookbackDays);
        reasons.Add(isRecent
            ? $"recent: posted {posted}"
            : $"skipped: posted {posted}, older than {_lookbackDays} days");

        // Rule 2, Keyword: "Non-Force Majeure" is tariff wording, not an event, so remove it first.
        // The Type field is ignored on purpose: every outage-list notice has Type "Planned Service Outage".
        var cleaned = text.Replace("Non-Force Majeure", "", StringComparison.OrdinalIgnoreCase);
        var matchedKeywords = new List<string>();
        foreach (var keyword in Keywords)
        {
            if (Contains(cleaned, keyword))
                matchedKeywords.Add(keyword);
        }
        if (Contains(notice.Subject, "capacity reduction"))
            matchedKeywords.Add("capacity reduction");
        var hasKeyword = matchedKeywords.Count > 0;
        reasons.Add(hasKeyword
            ? $"keyword: {string.Join(", ", matchedKeywords)}"
            : "skipped: no signal keyword");

        // Rule 3, Relevant: mentions Louisiana, Henry Hub, or a Henry Hub-linked pipeline. First match is reported.
        string? relevantName = null;
        if (Contains(text, "Louisiana"))
            relevantName = "Louisiana";
        else if (Contains(text, "Henry Hub"))
            relevantName = "Henry Hub";
        else
        {
            foreach (var pipeline in HenryHubPipelines)
            {
                if (text.Contains(pipeline, StringComparison.OrdinalIgnoreCase))
                {
                    relevantName = pipeline;
                    break;
                }
            }
        }
        var isRelevant = relevantName != null;
        reasons.Add(isRelevant
            ? $"relevant: mentions {relevantName}"
            : "skipped: no Louisiana / Henry Hub mention");

        return new SignalResult
        {
            Notice = notice,
            IsSignal = isRecent && hasKeyword && isRelevant,
            Reasons = reasons,
            Volume = FindVolume(notice),
        };
    }

    // First volume as written, subject first, then body. No unit conversion.
    private static string? FindVolume(Notice notice)
    {
        var match = VolumePattern.Match(notice.Subject);
        if (!match.Success)
            match = VolumePattern.Match(notice.Body);
        return match.Success ? match.Value : null;
    }

    private static bool Contains(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
