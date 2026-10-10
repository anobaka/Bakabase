using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/post-parser/task/summary")]
public sealed class PostParserTaskSummaryController(IPostParserTaskService service) : Controller
{
    [HttpGet]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetPostParserTaskSummary")]
    public async Task<SingletonResponse<TaskSummary>> Get([FromQuery] PostParserSource? source = null)
    {
        return new SingletonResponse<TaskSummary>(await service.GetSummary(source));
    }
}
