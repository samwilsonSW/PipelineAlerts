using PipelineAlerts.Models;

namespace PipelineAlerts.Interfaces;

public interface ISignalEvaluator
{
    SignalResult Evaluate(Notice notice, DateTimeOffset nowUtc);
}
