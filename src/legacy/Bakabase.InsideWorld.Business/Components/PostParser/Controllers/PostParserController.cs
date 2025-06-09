using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Controllers;

[ApiController]
[Route("~/post-parser")]
public class PostParserController(
    IPostParserTaskService service,
    IBakabaseLocalizer localizer,
    BTaskManager btm,
    PostParserTaskTrigger trigger) : ControllerBase
{
    [HttpGet("task/all")]
    [SwaggerOperation(OperationId = "GetAllPostParserTasks")]
    public async Task<ListResponse<PostParserTask>> GetAll()
    {
        var result = await service.GetAll();
        return new ListResponse<PostParserTask>(result);
    }

    [HttpPost("task")]
    [SwaggerOperation(OperationId = "AddPostParserTasks")]
    public async Task<BaseResponse> AddRange([FromBody] Dictionary<int, List<string>> sourceLinksMap)
    {
        await service.AddRange(sourceLinksMap.ToDictionary(d => (PostParserSource)d.Key, d => d.Value));
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("task/{id:int}")]
    [SwaggerOperation(OperationId = "DeletePostParserTask")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.Delete(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("task/all")]
    [SwaggerOperation(OperationId = "DeleteAllPostParserTasks")]
    public async Task<BaseResponse> DeleteAll()
    {
        await service.DeleteAll();
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("start")]
    [SwaggerOperation(OperationId = "StartAllPostParserTasks")]
    public async Task<BaseResponse> StartParsingAll()
    {
        await trigger.Start();
        return BaseResponseBuilder.Ok;
    }
}
