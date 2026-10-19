using PipelineAlerts.Models;

namespace PipelineAlerts.Tests.Notifiers;

/// <summary>
/// Test helper shared by the notifier tests: builds a trading signal around a notice with sensible defaults
/// (ANR, Planned Service Outage, IsSignal true, three reasons). Each test overrides only what it cares about.
/// </summary>
internal static class TestSignals
{
    public const string NoticeLink =
        "https://ebb.tceconnects.com/infopost/ReportViewer.aspx?/InfoPost/NoticesSubreport&pNoticeId=26252033&AssetNbr=3005";

    public static SignalResult Make(
        string subject = "CAPACITY REDUCTION - Southeast Area",
        string type = "Planned Service Outage",
        string? volume = "111 MMcf/d") => new()
    {
        Notice = new Notice
        {
            Pipeline = "ANR Pipeline Company",
            NoticeId = "26252033",
            Type = type,
            Status = "Initiate",
            Subject = subject,
            Body = "Compressor work near Henry Hub, Louisiana.",
            PostedUtc = new DateTimeOffset(2026, 10, 14, 13, 40, 34, TimeSpan.Zero),
            IsCritical = true,
            Link = new Uri(NoticeLink),
        },
        IsSignal = true,
        Reasons = ["recent: posted 2026-10-14", "keyword: capacity reduction", "relevant: mentions Louisiana"],
        Volume = volume,
    };
}
