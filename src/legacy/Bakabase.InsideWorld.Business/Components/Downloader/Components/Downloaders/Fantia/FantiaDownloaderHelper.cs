using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Fantia;

public class FantiaDownloaderHelper(
    IBOptionsManager<FantiaOptions> optionsManager,
    IDownloaderLocalizer localizer,
    HttpClient httpClient) : AbstractDownloaderHelper<FantiaOptions>(optionsManager, localizer, httpClient)
{
    public override ThirdPartyId ThirdPartyId => ThirdPartyId.Fantia;
    
    protected override string? CookieValidationUrl => "https://fantia.jp/api/v1/me";

    protected override async Task<bool> IsCookieValidResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return false;

        try
        {
            var content = await response.Content.ReadAsStringAsync();
            return content.Contains("\"user\":{") || content.Contains("\"id\":");
        }
        catch
        {
            return false;
        }
    }

    protected override async Task<BaseResponse> ValidateSpecificRequirementsAsync(DownloadTaskAddInputModel model)
    {
        // Fantia requires keys (creator IDs)
        return ValidateKeysRequired(model);
    }
}