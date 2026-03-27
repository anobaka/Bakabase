using System.Text.Json;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Modules.ThirdParty.Abstractions;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Steam;

public class SteamMetadataProvider : IMetadataProvider
{
    private readonly SteamClient _steamClient;
    private readonly IBOptions<SteamOptions> _options;
    private readonly IBOptions<AppOptions> _appOptions;

    public SteamMetadataProvider(SteamClient steamClient, IBOptions<SteamOptions> options, IBOptions<AppOptions> appOptions)
    {
        _steamClient = steamClient;
        _options = options;
        _appOptions = appOptions;
    }

    public DataOrigin Origin => DataOrigin.Steam;

    public List<SourceMetadataFieldInfo> GetPredefinedMetadataFields() =>
    [
        new(nameof(SteamMetadataField.Name), StandardValueType.String),
        new(nameof(SteamMetadataField.Type), StandardValueType.String),
        new(nameof(SteamMetadataField.ShortDescription), StandardValueType.String),
        new(nameof(SteamMetadataField.DetailedDescription), StandardValueType.String),
        new(nameof(SteamMetadataField.HeaderImage), StandardValueType.String),
        new(nameof(SteamMetadataField.CapsuleImage), StandardValueType.String),
        new(nameof(SteamMetadataField.Developers), StandardValueType.ListString),
        new(nameof(SteamMetadataField.Publishers), StandardValueType.ListString),
        new(nameof(SteamMetadataField.Genres), StandardValueType.ListString),
        new(nameof(SteamMetadataField.Categories), StandardValueType.ListString),
        new(nameof(SteamMetadataField.MetacriticScore), StandardValueType.Decimal),
        new(nameof(SteamMetadataField.ReleaseDate), StandardValueType.String),
    ];

    public async Task<SourceDetailedMetadata?> FetchMetadataAsync(string sourceKey, CancellationToken ct)
    {
        if (!int.TryParse(sourceKey, out var appId)) return null;

        var language = MetadataLanguageHelper.GetSteamLanguage(
            _options.Value.Language, _appOptions.Value.Language);
        var detail = await _steamClient.GetAppDetails(appId, language, ct);
        if (detail == null) return null;

        var result = new SourceDetailedMetadata
        {
            RawJson = JsonSerializer.Serialize(detail, JsonSerializerOptions.Web),
            CoverUrls = [detail.HeaderImage ?? $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg"],
            PredefinedFieldValues =
            {
                [nameof(SteamMetadataField.Name)] = detail.Name,
                [nameof(SteamMetadataField.Type)] = detail.Type,
                [nameof(SteamMetadataField.ShortDescription)] = detail.ShortDescription,
                [nameof(SteamMetadataField.DetailedDescription)] = detail.DetailedDescription,
                [nameof(SteamMetadataField.HeaderImage)] = detail.HeaderImage,
                [nameof(SteamMetadataField.CapsuleImage)] = detail.CapsuleImage,
                [nameof(SteamMetadataField.Developers)] = detail.Developers,
                [nameof(SteamMetadataField.Publishers)] = detail.Publishers,
                [nameof(SteamMetadataField.Genres)] = detail.Genres?.Select(g => g.Description).Where(d => d != null).ToList(),
                [nameof(SteamMetadataField.Categories)] = detail.Categories?.Select(c => c.Description).Where(d => d != null).ToList(),
                [nameof(SteamMetadataField.MetacriticScore)] = detail.Metacritic != null ? (decimal)detail.Metacritic.Score : null,
                [nameof(SteamMetadataField.ReleaseDate)] = detail.ReleaseDate?.Date,
            }
        };

        return result;
    }
}
