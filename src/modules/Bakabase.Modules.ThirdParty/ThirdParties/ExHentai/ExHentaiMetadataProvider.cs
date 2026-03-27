using System.Text.Json;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

public class ExHentaiMetadataProvider : IMetadataProvider
{
    private readonly ExHentaiClient _exHentaiClient;

    public ExHentaiMetadataProvider(ExHentaiClient exHentaiClient)
    {
        _exHentaiClient = exHentaiClient;
    }

    public DataOrigin Origin => DataOrigin.ExHentai;

    public List<SourceMetadataFieldInfo> GetPredefinedMetadataFields() =>
    [
        new(nameof(ExHentaiMetadataField.Name), StandardValueType.String),
        new(nameof(ExHentaiMetadataField.RawName), StandardValueType.String),
        new(nameof(ExHentaiMetadataField.Introduction), StandardValueType.String),
        new(nameof(ExHentaiMetadataField.Rate), StandardValueType.Decimal),
        new(nameof(ExHentaiMetadataField.Category), StandardValueType.String),
        new(nameof(ExHentaiMetadataField.CoverUrl), StandardValueType.String),
        new(nameof(ExHentaiMetadataField.FileCount), StandardValueType.Decimal),
        new(nameof(ExHentaiMetadataField.PageCount), StandardValueType.Decimal),
    ];

    public async Task<SourceDetailedMetadata?> FetchMetadataAsync(string sourceKey, CancellationToken ct)
    {
        var url = $"https://exhentai.org/g/{sourceKey}/";
        var detail = await _exHentaiClient.ParseDetail(url, false);
        if (detail == null) return null;

        var result = new SourceDetailedMetadata
        {
            RawJson = JsonSerializer.Serialize(detail, JsonSerializerOptions.Web),
            CoverUrls = !string.IsNullOrEmpty(detail.CoverUrl) ? [detail.CoverUrl] : null,
            PredefinedFieldValues =
            {
                [nameof(ExHentaiMetadataField.Name)] = detail.Name,
                [nameof(ExHentaiMetadataField.RawName)] = detail.RawName,
                [nameof(ExHentaiMetadataField.Introduction)] = detail.Introduction,
                [nameof(ExHentaiMetadataField.Rate)] = detail.Rate != 0 ? detail.Rate : null,
                [nameof(ExHentaiMetadataField.Category)] = detail.Category.ToString(),
                [nameof(ExHentaiMetadataField.CoverUrl)] = detail.CoverUrl,
                [nameof(ExHentaiMetadataField.FileCount)] = (decimal)detail.FileCount,
                [nameof(ExHentaiMetadataField.PageCount)] = (decimal)detail.PageCount,
            }
        };

        // Tags are dynamic fields: each tag group -> List<string>
        if (detail.Tags is { Count: > 0 })
        {
            foreach (var (group, tags) in detail.Tags)
            {
                result.CustomFieldValues[group] = tags.ToList();
            }
        }

        return result;
    }
}
