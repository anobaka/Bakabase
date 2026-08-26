using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Text;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Service.Models.Input;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/text")]
    public class TextController(ITextVocabularyService vocabulary, ITextOps textOps) : Controller
    {
        /// <summary>
        /// Builtin and user-defined types in one list — the single source for pickers and the
        /// management page.
        /// </summary>
        [HttpGet("type")]
        [SwaggerOperation(OperationId = "GetAllTextTypes")]
        public async Task<ListResponse<TextTypeDescriptor>> GetTypes() => new(await vocabulary.GetTypes());

        [HttpPost("type")]
        [SwaggerOperation(OperationId = "AddTextType")]
        public async Task<SingletonResponse<TextTypeDescriptor>> AddType([FromBody] TextTypeAddInputModel model) =>
            new(await vocabulary.AddType(model.Name, model.Shape, model.Description));

        [HttpPut("type/{id}")]
        [SwaggerOperation(OperationId = "RenameTextType")]
        public async Task<BaseResponse> RenameType(int id, [FromBody] TextTypePatchInputModel model)
        {
            await vocabulary.RenameType(id, model.Name);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("type/{id}")]
        [SwaggerOperation(OperationId = "DeleteTextType")]
        public async Task<BaseResponse> DeleteType(int id)
        {
            await vocabulary.DeleteType(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("type/{typeId}/entry")]
        [SwaggerOperation(OperationId = "GetTextEntries")]
        public async Task<ListResponse<TextEntryValue>> GetEntries(int typeId) =>
            new(await vocabulary.GetEntries(typeId));

        [HttpPost("type/{typeId}/entry")]
        [SwaggerOperation(OperationId = "AddTextEntry")]
        public async Task<SingletonResponse<TextEntryValue>> AddEntry(int typeId,
            [FromBody] TextEntryAddInputModel model) =>
            new(await vocabulary.AddEntry(typeId, model.Value1, model.Value2));

        [HttpPost("type/{typeId}/entry/batch")]
        [SwaggerOperation(OperationId = "AddTextEntries")]
        public async Task<BaseResponse> AddEntries(int typeId, [FromBody] List<TextEntryAddInputModel> models)
        {
            await vocabulary.AddEntries(typeId, models.Select(m => (m.Value1, m.Value2)));
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("entry/{id}")]
        [SwaggerOperation(OperationId = "PatchTextEntry")]
        public async Task<BaseResponse> PatchEntry(int id, [FromBody] TextEntryPatchInputModel model)
        {
            await vocabulary.PatchEntry(id, model.Value1, model.Value2);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("entry/{id}")]
        [SwaggerOperation(OperationId = "DeleteTextEntry")]
        public async Task<BaseResponse> DeleteEntry(int id)
        {
            await vocabulary.DeleteEntry(id);
            return BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// Resolved view of a type — what a node configuration form shows when previewing a set.
        /// </summary>
        [HttpGet("type/{typeId}/set")]
        [SwaggerOperation(OperationId = "ResolveTextSet")]
        public async Task<SingletonResponse<TextSet>> ResolveSet(int typeId) => new(await vocabulary.ResolveSet(typeId));

        [HttpPost("seeds")]
        [SwaggerOperation(OperationId = "EnsureTextSeeds")]
        public async Task<BaseResponse> EnsureSeeds()
        {
            await vocabulary.EnsureSeeds();
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("clean")]
        [SwaggerOperation(OperationId = "CleanText")]
        public async Task<SingletonResponse<string>> Clean(string text) => new(await textOps.Clean(text));
    }
}
