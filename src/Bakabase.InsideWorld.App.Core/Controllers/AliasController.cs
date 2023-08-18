using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/alias")]
    public class AliasController : Controller
    {
        private readonly AliasService _aliasService;
        private readonly IGuiAdapter _guiAdapter;

        public AliasController(AliasService aliasService, IGuiAdapter guiAdapter)
        {
            _aliasService = aliasService;
            _guiAdapter = guiAdapter;
        }

        [SwaggerOperation(OperationId = "GetAlias")]
        [HttpGet("{id}")]
        public async Task<SingletonResponse<Alias>> Get(int id)
        {
            return new SingletonResponse<Alias>(await _aliasService.GetByKey(id));
        }

        [SwaggerOperation(OperationId = "SearchAliases")]
        [HttpGet]
        public async Task<SearchResponse<AliasDto>> Search(AliasSearchRequestModel model)
        {
            return await _aliasService.Search(model);
        }

        [SwaggerOperation(OperationId = "UpdateAlias")]
        [HttpPut("{id}")]
        public async Task<BaseResponse> Update(int id, [FromBody] AliasUpdateRequestModel model)
        {
            return await _aliasService.UpdateByKey(id, model);
        }

        [SwaggerOperation(OperationId = "CreateAlias")]
        [HttpPost]
        public async Task<BaseResponse> Create([FromBody] AliasCreateRequestModel model)
        {
            return await _aliasService.Create(model);
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(OperationId = "RemoveAlias")]
        public async Task<BaseResponse> Delete(int id)
        {
            return await _aliasService.RemoveByKey(id);
        }

        [HttpPost("export")]
        [SwaggerOperation(OperationId = "ExportAliases")]
        public async Task<BaseResponse> Export()
        {
            var downloadsDirectory = _guiAdapter.GetDownloadsDirectory();
            if (!Directory.Exists(downloadsDirectory))
            {
                return BaseResponseBuilder.Build(ResponseCode.SystemError, "Can not find downloads directory");
            }

            var data = await _aliasService.Export();
            var fileName = Path.Combine(downloadsDirectory, $"alias-{DateTime.Now:yyyyMMddHHmmss}.csv");
            await System.IO.File.WriteAllTextAsync(fileName, data, Encoding.UTF8);
            await FileService.Open(fileName, true);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("import")]
        [SwaggerOperation(OperationId = "ImportAliases")]
        public async Task<BaseResponse> Import(IFormFile file)
        {
            return await _aliasService.Import(file.OpenReadStream());
        }
    }
}