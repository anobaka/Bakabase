using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Acquisition.Extensions;

public static class AcquisitionLeadExtensions
{
    /// <summary>
    /// The lead kinds that are actually stored. <see cref="AcquisitionLeadKind.PlatformHolding"/> is
    /// derived from the resource's source links, and <see cref="AcquisitionLeadKind.Manual"/>
    /// describes a choice rather than a place to get files from — neither is a row.
    /// </summary>
    public static readonly IReadOnlySet<AcquisitionLeadKind> StorableKinds = new HashSet<AcquisitionLeadKind>
    {
        AcquisitionLeadKind.SharedPage,
        AcquisitionLeadKind.SharedDocument,
        AcquisitionLeadKind.DirectUrl,
        AcquisitionLeadKind.Magnet
    };

    /// <summary>
    /// Makes two spellings of the same link compare equal, so importing the same list twice does not
    /// attach it twice. Only the parts that are case-insensitive by definition are touched: for an
    /// http(s) URL the scheme and host, which <see cref="Uri"/> lowercases on its own. Paths and
    /// query strings are left exactly as given — servers do treat those as case-sensitive.
    /// </summary>
    public static string NormalizeLeadValue(this string value)
    {
        var trimmed = value.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.AbsoluteUri;
        }

        return trimmed;
    }

    public static AcquisitionLead ToDomainModel(this AcquisitionLeadDbModel dbModel) => new()
    {
        Id = dbModel.Id,
        ResourceId = dbModel.ResourceId,
        Kind = dbModel.Kind,
        Value = dbModel.Value,
        Origin = dbModel.Origin,
        Note = dbModel.Note,
        LastUsedAt = dbModel.LastUsedAt,
        LastResult = dbModel.LastResult,
        CreatedAt = dbModel.CreatedAt
    };
}
