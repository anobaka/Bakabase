using Bakabase.Modules.AI.Models.Db;

namespace Bakabase.Modules.AI.Services;

public interface IAigcArtifactService
{
    Task<IReadOnlyList<AigcGenerationRunDbModel>> GetRunsAsync(int? generatorId, CancellationToken ct = default);
    Task<AigcGenerationRunDbModel?> GetRunAsync(int runId, CancellationToken ct = default);
    Task DeleteRunAsync(int runId, CancellationToken ct = default);

    Task<IReadOnlyList<AigcArtifactDbModel>> GetArtifactsAsync(int? generatorId, int? runId, CancellationToken ct = default);
    Task DeleteArtifactAsync(int artifactId, CancellationToken ct = default);
}
