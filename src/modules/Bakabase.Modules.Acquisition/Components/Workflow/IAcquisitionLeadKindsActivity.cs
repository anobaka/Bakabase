using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Acquisition.Components.Workflow;

/// <summary>The source boundary declares which saved leads its recipe can consume.</summary>
public interface IAcquisitionLeadKindsActivity
{
    IReadOnlyList<AcquisitionLeadKind>? AcceptedLeadKinds { get; }
}
