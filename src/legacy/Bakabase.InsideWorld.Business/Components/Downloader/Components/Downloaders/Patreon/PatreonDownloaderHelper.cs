using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Patreon;

public class PatreonDownloaderHelper(
    IBOptionsManager<PatreonOptions> optionsManager,
    IDownloaderLocalizer localizer,
    HttpClient httpClient) : AbstractDownloaderHelper<PatreonOptions>(optionsManager, localizer, httpClient)
{
    public override ThirdPartyId ThirdPartyId => ThirdPartyId.Patreon;
    
    protected override string? CookieValidationUrl => "https://www.patreon.com/api/current_user";
    
    protected override async Task<bool> IsCookieValidResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return false;

        try
        {
            var content = await response.Content.ReadAsStringAsync();
            return content.Contains("\"data\":{") && content.Contains("\"type\":\"user\"");
        }
        catch
        {
            return false;
        }
    }

    protected override async Task<BaseResponse> ValidateSpecificRequirementsAsync(DownloadTaskAddInputModel model)
    {
        // Patreon requires keys (creator IDs or campaign URLs)
        return ValidateKeysRequired(model);
    }
}