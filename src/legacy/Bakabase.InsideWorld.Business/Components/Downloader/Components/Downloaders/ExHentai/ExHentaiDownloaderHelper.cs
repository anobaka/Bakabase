using System;
using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

public class ExHentaiDownloaderHelper(
    IBOptionsManager<ExHentaiOptions> optionsManager,
    IDownloaderLocalizer localizer,
    HttpClient httpClient) : AbstractDownloaderHelper<ExHentaiOptions>(optionsManager, localizer, httpClient)
{
    public override ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;
    
    protected override string? CookieValidationUrl => "https://exhentai.org/";
    
    protected override async Task<bool> IsCookieValidResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return false;

        try
        {
            var content = await response.Content.ReadAsStringAsync();
            // ExHentai validation: if we can access the page without being redirected to e-hentai
            return !content.Contains("e-hentai.org") && content.Contains("exhentai");
        }
        catch
        {
            return false;
        }
    }

    protected override async Task<BaseResponse> ValidateCommonRequirementsAsync(DownloadTaskAddInputModel model)
    {
        // First run the base common validation
        var baseValidation = await base.ValidateCommonRequirementsAsync(model);
        if (!baseValidation.IsSuccess())
        {
            return baseValidation;
        }

        // ExHentai-specific common validation
        // Validate rate limiting settings - ExHentai requires at least 1 second
        if (model.Interval.HasValue && model.Interval.Value < 1000)
        {
            return BaseResponseBuilder.BuildBadRequest(
                "ExHentai requires minimum 1000ms interval to avoid rate limiting");
        }

        return BaseResponseBuilder.Ok;
    }

    protected override async Task<BaseResponse> ValidateSpecificRequirementsAsync(DownloadTaskAddInputModel model)
    {
        // Validate that keys are required and present
        var keysValidation = ValidateKeysRequired(model);
        if (!keysValidation.IsSuccess())
        {
            return keysValidation;
        }

        // Custom validation: Check if keys are valid URLs or gallery IDs
        foreach (var key in model.Keys!)
        {
            if (!IsValidExHentaiKey(key))
            {
                return BaseResponseBuilder.BuildBadRequest(
                    $"Invalid ExHentai key format: {key}. Must be URL or gallery ID.");
            }
        }

        return BaseResponseBuilder.Ok;
    }

    private static bool IsValidExHentaiKey(string key)
    {
        // Check if it's a URL
        if (Uri.TryCreate(key, UriKind.Absolute, out var uri))
        {
            return uri.Host.Contains("exhentai.org") || uri.Host.Contains("e-hentai.org");
        }

        // Check if it's a numeric gallery ID
        return long.TryParse(key, out var galleryId) && galleryId > 0;
    }
}