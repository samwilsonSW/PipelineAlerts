namespace PipelineAlerts.Models;

public sealed record Notice
{
    public required string Pipeline { get; init; }
    public required string NoticeId { get; init; }
    public string? PriorNoticeId { get; init; }
    public required string Type { get; init; }
    public required string Status { get; init; } 
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public string? Location { get; init; }
    public required DateTimeOffset PostedUtc { get; init; }
    public DateTimeOffset? EffectiveUtc { get; init; }
    public DateTimeOffset? EndUtc { get; init; }
    public required bool IsCritical { get; init; }
    public required Uri Link { get; init; }
}