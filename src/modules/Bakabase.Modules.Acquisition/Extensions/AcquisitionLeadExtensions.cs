using System.Text.Json;
using System.Text.Json.Nodes;
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
        AcquisitionLeadKind.Magnet,
        AcquisitionLeadKind.Torrent
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

    /// <summary>Enrich an unknown plan, but do not silently replace an executable saved plan.</summary>
    public static bool CanAcceptExtractionPlan(string? existingJson, string? incomingJson)
    {
        if (existingJson == null || incomingJson == null || existingJson == incomingJson) return true;
        try
        {
            var existing = JsonNode.Parse(existingJson);
            var incoming = JsonNode.Parse(incomingJson);
            if (existing is not JsonObject || incoming is not JsonObject) return false;
            var oldRequirement = (existing["requirement"] ?? existing["Requirement"])?.GetValue<string>();
            var newRequirement = (incoming["requirement"] ?? incoming["Requirement"])?.GetValue<string>();
            var oldSteps = existing["steps"] ?? existing["Steps"];
            var oldIncomplete = oldRequirement == "unknown" || (oldRequirement == "required" && oldSteps is JsonArray {Count: 0});
            if (oldIncomplete && newRequirement is "required" or "notRequired") return true;
            return oldRequirement == newRequirement && JsonNode.DeepEquals(
                existing["steps"] ?? existing["Steps"], incoming["steps"] ?? incoming["Steps"]);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return false; }
    }

    public static AcquisitionLead ToDomainModel(this AcquisitionLeadDbModel dbModel) => new()
    {
        Id = dbModel.Id,
        ResourceId = dbModel.ResourceId,
        Kind = dbModel.Kind,
        Value = dbModel.Value,
        Origin = dbModel.Origin,
        Note = dbModel.Note,
        AccessCode = dbModel.AccessCode,
        Password = dbModel.Password,
        SourceReference = dbModel.SourceReference,
        IsResolved = dbModel.IsResolved,
        ExtractionPlanJson = dbModel.ExtractionPlanJson,
        LastUsedAt = dbModel.LastUsedAt,
        LastResult = dbModel.LastResult,
        CreatedAt = dbModel.CreatedAt
    };
}
