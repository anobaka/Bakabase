using Bakabase.Infrastructures.Components.App;
using Bakabase.Service.Models.View;
using Bakabase.Service.Services;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/app/deployment-paths")]
public sealed class DeploymentPathsController(AppService appService, DeploymentPathDisplay display) : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    // Same operator boundary as /app/info; host filesystem details are not an anonymous capability.
    [SwaggerOperation(OperationId = "GetDeploymentPaths")]
    public SingletonResponse<DeploymentPathsViewModel> Get()
    {
        var info = appService.AppInfo;
        return new(display.Describe([info.AppDataPath, info.AnchorPath, info.DefaultDataPath, info.DataPath,
            info.TempFilesPath, info.LogPath, info.BackupPath]));
    }
}
