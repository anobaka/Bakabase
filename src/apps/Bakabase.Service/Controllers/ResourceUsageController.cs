using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.View;
using Bakabase.Service.Services;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/app/resource-usage")]
public class ResourceUsageController(ResourceUsageService service) : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetResourceUsage")]
    public SingletonResponse<ResourceUsageViewModel> Get() => new(service.GetSnapshot());
}
