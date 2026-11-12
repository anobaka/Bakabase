using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.View;
using Bakabase.Service.Services;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/file/storage-capacity")]
public sealed class FileStorageCapacityController(PathStorageCapacityService capacity) : ControllerBase
{
    [HttpGet]
    [RemoteAccessible(PathParameters = [nameof(path)])]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [SwaggerOperation(OperationId = "GetPathStorageCapacity")]
    public async Task<SingletonResponse<StorageCapacityViewModel>> Get([FromQuery] string path, CancellationToken ct) =>
        new(await capacity.GetAsync(path, ct));
}
