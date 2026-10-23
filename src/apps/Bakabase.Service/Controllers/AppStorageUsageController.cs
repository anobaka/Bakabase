using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.View;
using Bakabase.Service.Services;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/app/storage-usage")]
public class AppStorageUsageController(AppStorageUsageService service) : Controller
{
    [HttpGet]
    [RemoteAccessible]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [SwaggerOperation(OperationId = "GetAppStorageUsage")]
    public async Task<SingletonResponse<AppStorageUsageViewModel>> Get([FromQuery] bool forceRefresh = false,
        CancellationToken ct = default) => new(await service.GetAsync(forceRefresh, ct));
}
