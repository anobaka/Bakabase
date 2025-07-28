using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Cien;

public class CienDownloaderHelper(
    IBOptionsManager<CienOptions> optionsManager,
    IDownloaderLocalizer localizer,
    HttpClient httpClient) : AbstractDownloaderHelper<CienOptions>(optionsManager, localizer, httpClient)
{
    public override ThirdPartyId ThirdPartyId => ThirdPartyId.Cien;
    
    protected override string? CookieValidationUrl => "https://ci-en.dlsite.com/api/user";
    
    protected override async Task<bool> IsCookieValidResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return false;

        try
        {
            var content = await response.Content.ReadAsStringAsync();
            return content.Contains("\"id\":") || content.Contains("\"user\":");
        }
        catch
        {
            return false;
        }
    }

    protected override async Task<BaseResponse> ValidateSpecificRequirementsAsync(DownloadTaskAddInputModel model)
    {
        // Cien requires keys (creator IDs)
        return ValidateKeysRequired(model);
    }
}