using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.Modules.Alias.Abstractions.Models.Domain;
using Bakabase.Modules.Alias.Abstractions.Services;
using Bakabase.Modules.Alias.Models.Input;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Office.Excel;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
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

        [HttpGet("xlsx")]
        [SwaggerOperation(OperationId = "ExportAliases")]
        public async Task<IActionResult> Export()
        {
            var aliases = await aliasService.GetAll();
            var preferredCandidatesMap = aliases.GroupBy(x => x.Preferred ?? x.Text)
                .ToDictionary(d => d.Key, d => d.Where(a => a.Text != d.Key).ToList());

            var lines = new List<SimpleColumn[]>();
            foreach (var a in preferredCandidatesMap)
            {
                lines.Add(new[] {a.Key}.Concat(a.Value.Select(b => b.Text)).Distinct().Where(t => t.IsNotEmpty()).Select(b => new SimpleColumn(b)).ToArray());
            }
            var bytes = ExcelUtils.CreateExcel(new ExcelData(lines));

            var filename = $"alias-{DateTime.Now:yyyyMMddHHmmss}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
        }

        [HttpPost("import")]
        [SwaggerOperation(OperationId = "ImportAliases")]
        public async Task<BaseResponse> Import(string path)
        {
            var stream = System.IO.File.OpenRead(path);
            var rawData = CsvUtils.ReadAllLines(stream);
            var data = rawData.Where(t => t.Any()).Select(t => t.Distinct().ToArray()).ToList();
            var uniques = new Dictionary<string, int>();
            var duplicates = new HashSet<string>();
            for (var i = 0; i < data.Count; i++)
            {
                foreach (var d in data[i])
                {
                    if (!uniques.TryAdd(d, i))
                    {
                        duplicates.Add(d);
                    }
                }
            }

            if (duplicates.Any())
            {
                return BaseResponseBuilder.BuildBadRequest(
                    $"Duplicate aliases are found: {string.Join(',', duplicates)}");
            }

            await aliasService.MergeGroups(data.ToArray());

            return BaseResponseBuilder.Ok;
        }
    }
}