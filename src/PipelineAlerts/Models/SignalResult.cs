namespace PipelineAlerts.Models;

public sealed record SignalResult
{
    public required Notice Notice { get; init; }
    public required bool IsSignal { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public string? Volume { get; init; }
}
