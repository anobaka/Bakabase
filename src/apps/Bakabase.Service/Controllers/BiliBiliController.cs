using System.Threading.Tasks;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
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
            var fs = await client.GetFavorites();
            return new(fs);
        }
    }
}