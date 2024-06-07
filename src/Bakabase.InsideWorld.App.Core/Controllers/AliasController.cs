using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.InsideWorld.Business.Components.Legacy.Services;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.Alias.Abstractions.Models.Domain;
using Bakabase.Modules.Alias.Abstractions.Services;
using Bakabase.Modules.Alias.Models.Input;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/alias")]
    public class AliasController(IAliasService aliasService, IGuiAdapter guiAdapter) : Controller
    {
        [SwaggerOperation(OperationId = "SearchAliasGroups")]
        [HttpGet]
        public async Task<SearchResponse<Alias>> SearchGroups(AliasSearchInputModel model)
        {
            return await aliasService.SearchGroups(model);
        }

        [SwaggerOperation(OperationId = "PatchAlias")]
        [HttpPut]
        public async Task<BaseResponse> Patch(string text, [FromBody] AliasPatchInputModel model)
        {
            return await aliasService.Patch(text, model);
        }

        [SwaggerOperation(OperationId = "AddAlias")]
        [HttpPost]
        public async Task<BaseResponse> Add([FromBody] AliasAddInputModel model)
        {
            return await aliasService.Add(model);
        }

        [HttpDelete]
        [SwaggerOperation(OperationId = "DeleteAlias")]
        public async Task<BaseResponse> Delete(string text)
        {
            return await aliasService.Delete(text);
        }

        [HttpDelete("groups")]
        [SwaggerOperation(OperationId = "DeleteAliasGroups")]
        public async Task<BaseResponse> DeleteGroups(string[] preferredTexts)
        {
            return await aliasService.DeleteGroups(preferredTexts);
        }

        [HttpPut("merge")]
        [SwaggerOperation(OperationId = "MergeAliasGroups")]
        public async Task<BaseResponse> MergeGroups(string[] preferredTexts)
        {
            return await aliasService.MergeGroups(preferredTexts);
        }

        // [HttpPost("export")]
        // [SwaggerOperation(OperationId = "ExportAliases")]
        // public async Task<BaseResponse> Export()
        // {
        //     var downloadsDirectory = guiAdapter.GetDownloadsDirectory();
        //     if (!Directory.Exists(downloadsDirectory))
        //     {
        //         return BaseResponseBuilder.Build(ResponseCode.SystemError, "Can not find downloads directory");
        //     }
        //
        //     var data = await aliasService.Export();
        //     var fileName = Path.Combine(downloadsDirectory, $"alias-{DateTime.Now:yyyyMMddHHmmss}.csv");
        //     await System.IO.File.WriteAllTextAsync(fileName, data, Encoding.UTF8);
        //     await FileService.Open(fileName, true);
        //     return BaseResponseBuilder.Ok;
        // }

        // [HttpPost("import")]
        // [SwaggerOperation(OperationId = "ImportAliases")]
        // public async Task<BaseResponse> Import(IFormFile file)
        // {
        //     return await _aliasService.Import(file.OpenReadStream());
        // }
    }
}