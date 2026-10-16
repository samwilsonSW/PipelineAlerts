using PipelineAlerts.Models;

namespace PipelineAlerts.Interfaces;

public interface INoticeSource
{
    string Name { get; }
    Task<IReadOnlyList<Notice>> FetchAsync(CancellationToken ct);
}