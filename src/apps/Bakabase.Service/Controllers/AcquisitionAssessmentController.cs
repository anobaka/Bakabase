using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Components.Acquisition.Planning;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition/tasks")]
public sealed class AcquisitionAssessmentController(BakabaseDbContext db) : ControllerBase
{
    [HttpGet("{taskId:int}/runs/{runId:int}/assessment-io")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetAcquisitionAssessmentIo")]
    public async Task<SingletonResponse<AcquisitionAssessmentIoView>> Get(int taskId, int runId,
        [FromQuery] string attemptId, CancellationToken ct)
    {
        var detail = await AcquisitionAssessmentInspection.ReadAsync(db, taskId, runId, attemptId, ct);
        return detail == null ? SingletonResponseBuilder<AcquisitionAssessmentIoView>.NotFound : new(detail);
    }
}
