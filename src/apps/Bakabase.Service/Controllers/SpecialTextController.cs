using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/special-text")]
    public class SpecialTextController(ISpecialTextService textService) : Controller
    {
        [HttpGet]
        [SwaggerOperation(OperationId = "GetAllSpecialTexts")]
        public async Task<SingletonResponse<Dictionary<int, List<SpecialText>>>> GetAll()
        {
            return new((await textService.GetAll()).GroupBy(d => d.Type)
                .ToDictionary(d => (int) d.Key, d => d.ToList()));
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(OperationId = "DeleteSpecialText")]
        public async Task<BaseResponse> Delete(int id)
        {
            return await textService.DeleteByKey(id);
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "AddSpecialText")]
        public async Task<SingletonResponse<SpecialText>> Add([FromBody] SpecialTextAddInputModel model)
        {
            return await textService.Add(model);
        }

        [HttpPut("{id}")]
        [SwaggerOperation(OperationId = "PatchSpecialText")]
        public async Task<BaseResponse> Patch(int id, [FromBody] SpecialTextPatchInputModel model)
        {
            return await textService.Patch(id, model);
        }

        [HttpPost("prefabs")]
        [SwaggerOperation(OperationId = "AddSpecialTextPrefabs")]
        public async Task<BaseResponse> AddPrefabs()
        {
            return await textService.AddPrefabs();
        }

        [HttpPost("pretreatment")]
        [SwaggerOperation(OperationId = "PretreatText")]
        public async Task<SingletonResponse<string>> Pretreat(string text)
        {
            var result = await textService.Pretreatment(text);
            return new SingletonResponse<string>(result);
        }
    }
}