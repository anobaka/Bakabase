using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition/tasks")]
public sealed class AcquisitionAssessmentController(BakabaseDbContext db, AssessmentCallSnapshotStore snapshots) : ControllerBase
{
    [HttpGet("{taskId:int}/runs/{runId:int}/assessment-io")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetAcquisitionAssessmentIo")]
    public async Task<SingletonResponse<AcquisitionAssessmentIoView>> Get(int taskId, int runId,
        [FromQuery] string attemptId, CancellationToken ct)
    {
        var detail = await AcquisitionAssessmentInspection.ReadAsync(db, taskId, runId, attemptId, ct, snapshots);
        return detail == null ? SingletonResponseBuilder<AcquisitionAssessmentIoView>.NotFound : new(detail);
    }

    [HttpGet("{taskId:int}/runs/{runId:int}/assessment-io/text")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetAcquisitionAssessmentText")]
    public async Task<SingletonResponse<AcquisitionAssessmentTextPage>> Text(int taskId, int runId,
        [FromQuery] string attemptId, [FromQuery] string callId, [FromQuery] string direction,
        [FromQuery] int offset = 0, [FromQuery] int length = 32768, [FromQuery] string? contentId = null,
        CancellationToken ct = default)
    {
        var page = await AcquisitionAssessmentInspection.ReadPageAsync(db, taskId, runId, attemptId, callId,
            direction, offset, length, contentId, ct, snapshots);
        Response.Headers.CacheControl = "no-store";
        return page == null ? SingletonResponseBuilder<AcquisitionAssessmentTextPage>.NotFound : new(page);
    }
}
