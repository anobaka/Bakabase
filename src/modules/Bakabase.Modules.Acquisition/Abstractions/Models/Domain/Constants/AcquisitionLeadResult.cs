namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

/// <summary>
/// How the last attempt to obtain the resource through this lead ended. A lead that keeps failing is
/// worth showing differently from one nobody has tried.
/// </summary>
public enum AcquisitionLeadResult
{
    Succeeded = 1,
    Failed = 2
}
