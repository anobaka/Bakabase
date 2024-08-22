using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models;
using Bakabase.InsideWorld.Business.Configurations;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/bilibili")]
    public class BiliBiliController : Controller
    {
        private readonly InsideWorldOptionsManagerPool _optionsManager;

        public BiliBiliController(InsideWorldOptionsManagerPool optionsManager)
        {
            _optionsManager = optionsManager;
        }

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