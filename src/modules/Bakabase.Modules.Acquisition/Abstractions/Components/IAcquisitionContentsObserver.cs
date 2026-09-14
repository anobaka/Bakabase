using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

namespace Bakabase.Modules.Acquisition.Abstractions.Components;

/// <summary>Lets the host persist content locations without coupling file steps to an application result model.</summary>
public interface IAcquisitionContentsObserver
{
    Task OnContentsReadyAsync(AcquisitionStepContext context, AcquisitionWorkItem item,
        string directory, IReadOnlyList<string> files, CancellationToken ct);
}
