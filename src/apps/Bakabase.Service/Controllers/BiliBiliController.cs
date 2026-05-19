using System;
using System.Threading.Tasks;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/bilibili")]
    public class BiliBiliController : Controller
    {
        [HttpGet("favorites")]
        [SwaggerOperation(OperationId = "GetBiliBiliFavorites")]
        public async Task<ListResponse<Favorites>> GetFavorites(
            [FromServices] BilibiliClient client)
        {
            try
            {
                var fs = await client.GetFavorites();
                return new(fs);
            }
            catch (Exception ex)
            {
                // Most failures here are expired/invalid cookie — surface as
                // 400 with the localised message rather than letting Sentry
                // record a 500.
                return ListResponseBuilder<Favorites>.BuildBadRequest(ex.Message);
            }
        }
    }
}