using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.OAuth;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/oauth/providers")]
[RemoteAccessible(false)]
[OwnerUiOrigin("OAUTH_ORIGIN_REJECTED")]
public sealed class OAuthProvidersController(OAuthClientConfigurationStore configuration) : Controller
{
    [HttpGet("{providerId}/configuration")]
    [SwaggerOperation(OperationId = "GetOAuthProviderConfiguration")]
    public async Task<SingletonResponse<OAuthProviderConfigurationViewModel>> Get(string providerId, CancellationToken ct)
    {
        try { return new(await configuration.GetAsync(providerId, ct)); }
        catch (OAuthAuthorizationException error) { return SingletonResponseBuilder<OAuthProviderConfigurationViewModel>.BuildBadRequest(error.Code); }
    }

    [HttpPut("{providerId}/configuration")]
    [SwaggerOperation(OperationId = "SaveOAuthProviderConfiguration")]
    public async Task<SingletonResponse<OAuthProviderConfigurationViewModel>> Save(string providerId,
        [FromBody] OAuthProviderConfigurationInputModel input, CancellationToken ct)
    {
        try { return new(await configuration.SaveAsync(providerId, input, ct)); }
        catch (OAuthAuthorizationException error) { return SingletonResponseBuilder<OAuthProviderConfigurationViewModel>.BuildBadRequest(error.Code); }
    }
}
