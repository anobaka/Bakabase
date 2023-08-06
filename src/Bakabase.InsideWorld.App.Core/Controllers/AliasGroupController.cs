using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/alias-group")]
    public class AliasGroupController : Controller
    {
        private readonly AliasService _aliasService;

        public AliasGroupController(AliasService aliasService)
        {
            _aliasService = aliasService;
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(OperationId = "RemoveAliasGroup")]
        public async Task<BaseResponse> Remove(int id)
        {
            return await _aliasService.RemoveGroup(id);
        }


        [HttpPut("{id}")]
        [SwaggerOperation(OperationId = "MergeAliasGroup")]
        public async Task<BaseResponse> Merge(int id, [FromBody]AliasGroupUpdateRequestModel model)
        {
            return await _aliasService.MergeGroup(id, model);
        }
    }
}