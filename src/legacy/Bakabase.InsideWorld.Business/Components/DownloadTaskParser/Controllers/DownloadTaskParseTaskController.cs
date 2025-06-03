using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Controllers;

[ApiController]
[Route("~/download-task-parse-task")]
public class DownloadTaskParseTaskController(
    IDownloadTaskParseTaskService service,
    IBakabaseLocalizer localizer,
    BTaskManager btm) : ControllerBase
{
    [HttpGet("all")]
    [SwaggerOperation(OperationId = "GetAllDownloadTaskParseTasks")]
    public async Task<ListResponse<DownloadTaskParseTask>> GetAll()
    {
        var result = await service.GetAll();
        return new ListResponse<DownloadTaskParseTask>(result);
    }

    [HttpPost()]
    [SwaggerOperation(OperationId = "AddDownloadTaskParseTasks")]
    public async Task<BaseResponse> AddRange([FromBody] Dictionary<int, List<string>> sourceLinksMap)
    {
        await service.AddRange(sourceLinksMap.ToDictionary(d => (DownloadTaskParserSource)d.Key, d => d.Value));
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteDownloadTaskParseTask")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.Delete(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("all")]
    [SwaggerOperation(OperationId = "DeleteAllDownloadTaskParseTasks")]
    public async Task<BaseResponse> DeleteAll()
    {
        await service.DeleteAll();
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("start")]
    [SwaggerOperation(OperationId = "StartAllDownloadTaskParseTasks")]
    public async Task<BaseResponse> StartParsingAll()
    {
        var btmTaskBuilder = new BTaskHandlerBuilder
        {
            GetName = localizer.DownloadTaskParser_ParseAll_TaskName,
            Type = BTaskType.Any,
            ResourceType = BTaskResourceType.Any,
            CancellationToken = null,
            Id = "ParseAllDownloadTasks",
            Run = async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var sp = scope.ServiceProvider;
                var svc = sp.GetRequiredService<IDownloadTaskParseTaskService>();
                await svc.ParseAll(async p => await args.UpdateTask(t => t.Percentage = p),
                    async p => await args.UpdateTask(t => t.Process = p), args.PauseToken,
                    args.CancellationToken);
            },
            Level = BTaskLevel.Default,
        };

        btm.Enqueue(btmTaskBuilder);
        return BaseResponseBuilder.Ok;
    }
}
