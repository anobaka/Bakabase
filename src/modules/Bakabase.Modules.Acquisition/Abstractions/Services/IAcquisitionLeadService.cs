using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Input;

namespace Bakabase.Modules.Acquisition.Abstractions.Services;

/// <summary>
/// What came of trying to attach a lead.
/// </summary>
/// <param name="Lead">
/// The lead now attached to the requested resource. Null when the link already belongs to a
/// different one.
/// </param>
/// <param name="ConflictingResourceId">
/// The resource already holding this link, when that is not the resource asked for. A shared link
/// describes exactly one resource, so attaching it twice is a mistake worth naming rather than
/// silently allowing.
/// </param>
public record AcquisitionLeadAddResult(AcquisitionLead? Lead, int? ConflictingResourceId);

/// <summary>
/// Stores where a resource can be obtained from. Only sharing-channel leads are stored; a platform
/// the user holds the resource on is a <c>ResourceSourceLink</c> and is derived into a lead where
/// one is needed.
/// </summary>
public interface IAcquisitionLeadService
{
    Task<AcquisitionLead?> Get(int id);

    Task<List<AcquisitionLead>> GetByResourceId(int resourceId);

    Task<Dictionary<int, List<AcquisitionLead>>> GetByResourceIds(IReadOnlyCollection<int> resourceIds);

    /// <summary>
    /// Finds the lead carrying <paramref name="value"/>, whichever resource it is attached to.
    /// The value is normalized the same way stored values are, so a differently spelled URL still
    /// finds its lead.
    /// </summary>
    Task<AcquisitionLead?> FindByValue(AcquisitionLeadKind kind, string value);

    /// <exception cref="ArgumentOutOfRangeException">
    /// The kind is not one that is stored — see <c>AcquisitionLeadExtensions.StorableKinds</c>.
    /// </exception>
    Task<AcquisitionLeadAddResult> Add(int resourceId, AcquisitionLeadAddInputModel model);

    Task Delete(int id);

    Task DeleteByResourceIds(IEnumerable<int> resourceIds);

    /// <summary>
    /// Records that this lead was just used, and how it went.
    /// </summary>
    Task MarkUsed(int id, AcquisitionLeadResult result);
}
