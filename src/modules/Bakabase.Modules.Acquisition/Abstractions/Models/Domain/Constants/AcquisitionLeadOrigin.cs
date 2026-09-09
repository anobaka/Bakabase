namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

/// <summary>
/// Who put this lead here. Kept so a lead that turns out to be wrong can be traced back to whatever
/// produced it, and so a bulk import can be undone without touching what the user added by hand.
/// </summary>
public enum AcquisitionLeadOrigin
{
    User = 1,
    Subscription = 2,
    SharedListImport = 3,
    PostParser = 4
}
