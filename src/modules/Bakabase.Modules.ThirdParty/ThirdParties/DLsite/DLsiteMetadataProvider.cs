using System.Text.Json;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;

namespace Bakabase.Modules.ThirdParty.ThirdParties.DLsite;

public class DLsiteMetadataProvider : IMetadataProvider
{
    private readonly DLsiteClient _dlsiteClient;

    public DLsiteMetadataProvider(DLsiteClient dlsiteClient)
    {
        _dlsiteClient = dlsiteClient;
    }

    public DataOrigin Origin => DataOrigin.DLsite;

    public List<SourceMetadataFieldInfo> GetPredefinedMetadataFields() =>
    [
        new(nameof(DLsiteMetadataField.Introduction), StandardValueType.String),
        new(nameof(DLsiteMetadataField.Rating), StandardValueType.Decimal),
        new(nameof(DLsiteMetadataField.CoverUrls), StandardValueType.ListString),
    ];

    public async Task<SourceDetailedMetadata?> FetchMetadataAsync(string sourceKey, CancellationToken ct)
    {
        var detail = await _dlsiteClient.ParseWorkDetailById(sourceKey);
        if (detail == null) return null;

        var result = new SourceDetailedMetadata
        {
            RawJson = JsonSerializer.Serialize(detail, JsonSerializerOptions.Web),
            Name = detail.Name,
            CoverUrls = detail.CoverUrls?.ToList(),
            PredefinedFieldValues =
            {
                [nameof(DLsiteMetadataField.Introduction)] = detail.Introduction,
                [nameof(DLsiteMetadataField.Rating)] = detail.Rating,
                [nameof(DLsiteMetadataField.CoverUrls)] = detail.CoverUrls?.ToList(),
            }
        };

        // Dynamic fields from the right side of cover
        if (detail.PropertiesOnTheRightSideOfCover is { Count: > 0 })
        {
            foreach (var (key, values) in detail.PropertiesOnTheRightSideOfCover)
            {
                result.CustomFieldValues[key] = values;
            }
        }

        return result;
    }
}
