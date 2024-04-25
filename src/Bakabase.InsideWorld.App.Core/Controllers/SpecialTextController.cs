using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/special-text")]
    public class SpecialTextController : Controller
    {
        private readonly SpecialTextService _textService;

        public SpecialTextController(SpecialTextService textService)
        {
            _textService = textService;
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "GetAllSpecialText")]
        public async Task<SingletonResponse<Dictionary<int, List<SpecialText>>>> GetAll()
        {
            return new((await _textService.GetAll()).ToDictionary(d => (int) d.Key, d => d.Value));
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(OperationId = "DeleteSpecialText")]
        public async Task<BaseResponse> Remove(int id)
        {
            return await _textService.RemoveByKey(id);
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "CreateSpecialText")]
        public async Task<SingletonResponse<SpecialText>> Create([FromBody] SpecialTextCreateRequestModel model)
        {
            return await _textService.Add(model);
        }

        [HttpPut("{id}")]
        [SwaggerOperation(OperationId = "UpdateSpecialText")]
        public async Task<BaseResponse> Update(int id, [FromBody] SpecialTextUpdateRequestModel model)
        {
            return await _textService.UpdateByKey(id, model);
        }

        [HttpPost("prefabs")]
        [SwaggerOperation(OperationId = "AddSpecialTextPrefabs")]
        public async Task<BaseResponse> AddPrefabs()
        {
            return await _textService.AddPrefabs();
        }

        [HttpPost("pretreatment")]
        [SwaggerOperation(OperationId = "PretreatText")]
        public async Task<SingletonResponse<string>> Pretreat(string text)
        {
            var result = await _textService.Pretreatment(text);
            return new SingletonResponse<string>(result);
        }
    }
}