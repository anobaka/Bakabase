using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Services;

/// <summary>A feature owns its inference capacity, metadata and live queue state.</summary>
public interface ILlmFeatureExecutionPolicy
{
    string CapabilityId { get; }
    AiFeature Feature { get; }
    int Capacity { get; }
    LlmFeatureExecutionSnapshot GetSnapshot();
    Task<IDisposable> EnterAsync(CancellationToken ct = default);
}

public sealed record LlmFeatureExecutionSnapshot(string CapabilityId, AiFeature Feature,
    int Capacity, int Active, int Waiting);

public enum LlmFeatureExecutionStatus { Queued, Running, Released }

public sealed record LlmFeatureExecutionState(string CapabilityId, AiFeature Feature,
    LlmFeatureExecutionStatus Status);
