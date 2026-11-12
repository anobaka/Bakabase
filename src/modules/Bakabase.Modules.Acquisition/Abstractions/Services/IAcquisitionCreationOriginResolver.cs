using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

namespace Bakabase.Modules.Acquisition.Abstractions.Services;

/// <summary>A host adapter resolves producer references without coupling acquisition to producer modules.</summary>
public interface IAcquisitionCreationOriginResolver
{
    Task EnrichAsync(IReadOnlyList<AcquisitionTask> tasks, CancellationToken ct = default);
}
