using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Models.Input;

namespace Bakabase.Modules.AI.Services;

public interface IAigcGeneratorService
{
    Task<IReadOnlyList<AigcGeneratorView>> GetAllAsync(CancellationToken ct = default);
    Task<AigcGeneratorView?> GetAsync(int id, CancellationToken ct = default);
    Task<AigcGeneratorView> AddAsync(AigcGeneratorAddInputModel input, CancellationToken ct = default);
    Task<AigcGeneratorView> UpdateAsync(int id, AigcGeneratorUpdateInputModel input, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Creates a Pending run row and enqueues the AIGC generation BTask. Returns the run id.</summary>
    Task<int> TriggerRunAsync(int generatorId, AigcGenerationTriggerInputModel? input, CancellationToken ct = default);

    /// <summary>Imports existing files as artifacts (moves them under the generator's directory, creates Resources).</summary>
    Task<int> ImportArtifactsAsync(int generatorId, AigcArtifactImportInputModel input, CancellationToken ct = default);

    /// <summary>
    /// Imports ComfyUI API-format workflow JSON files (and folders containing them) and creates one
    /// AigcGenerator per unique workflow under the target provider. Duplicates (same content hash)
    /// against existing generators on this provider are skipped.
    /// </summary>
    Task<AigcGeneratorComfyUIImportResult> ImportComfyUIWorkflowsAsync(
        AigcGeneratorComfyUIImportInputModel input, CancellationToken ct = default);
}
