using PipelineAlerts.Models;

namespace PipelineAlerts.Interfaces;

public interface INotifier
{
    Task SendAsync(SignalResult signalResult, CancellationToken ct); 
}
