using System;
using System.Threading.Tasks;
using Bakabase.Service.Components.Acquisition.Planning;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>Local-owner maintenance on an exact saved revision. Does not accept file paths.</summary>
[ApiController]
[Route("~/acquisition/plans/{id:int}/superseded-delivery")]
public sealed class AcquisitionSupersededDeliveryController(AcquisitionSupersededDeliveryService deliveries) : ControllerBase
{
    [HttpPost("retire")]
    [SwaggerOperation(OperationId = "RetireSupersededAcquisitionDelivery")]
    public async Task<SingletonResponse<AcquisitionSupersededDeliveryResult>> Retire(int id,
        [FromBody] AcquisitionSupersededDeliveryInput input)
    {
        try { return new(await deliveries.RetireAsync(id, input, HttpContext.RequestAborted)); }
        catch (ArgumentException e) { return SingletonResponseBuilder<AcquisitionSupersededDeliveryResult>.BuildBadRequest(e.Message); }
    }
}
