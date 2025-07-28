using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Pixiv;

public class PixivDownloaderHelper(
    IBOptionsManager<PixivOptions> optionsManager,
    IDownloaderLocalizer localizer,
    HttpClient httpClient) : AbstractDownloaderHelper<PixivOptions>(optionsManager, localizer, httpClient)
{
    public override ThirdPartyId ThirdPartyId => ThirdPartyId.Pixiv;
    
    protected override string? CookieValidationUrl => "https://www.pixiv.net/ajax/user/self";
    
    protected override async Task<bool> IsCookieValidResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return false;

        try
        {
            var content = await response.Content.ReadAsStringAsync();
            return content.Contains("\"error\":false");
        }
        catch
        {
            return false;
        }
    }

    protected override async Task<BaseResponse> ValidateSpecificRequirementsAsync(DownloadTaskAddInputModel model)
    {
        // Different Pixiv task types have different key requirements
        return model.Type switch
        {
            1 => ValidateKeysRequired(model), // Search - requires keys
            2 => ValidateKeysRequired(model), // Ranking - requires keys
            3 => ValidateNoKeysRequired(model), // Following - no keys required
            _ => BaseResponseBuilder.BuildBadRequest($"Unknown Pixiv task type: {model.Type}")
        };
    }
}