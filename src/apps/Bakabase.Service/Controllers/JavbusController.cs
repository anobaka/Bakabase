using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.Javbus;
using Bakabase.Modules.ThirdParty.ThirdParties.Javbus.Models;
using Bakabase.Service.Models.Input;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/javbus")]
public class JavbusController(
    JavbusBatchDownloadService service,
    BTaskManager btm,
    IBakabaseLocalizer localizer,
    IBOptions<JavbusDownloaderOptions> options) : Controller
{
    public const string BatchDownloadTaskId = "JavbusBatchDownload";

    private const int DefaultConcurrency = 2;
    private const int DefaultDelayMs = 600;
    private const int DefaultSizeTolerancePercentage = 30;

    [HttpGet("batch-download")]
    [SwaggerOperation(OperationId = "GetJavbusBatchDownloadState")]
    public SingletonResponse<JavbusBatchDownloadState> GetBatchDownloadState()
    {
        return new SingletonResponse<JavbusBatchDownloadState>(service.GetState());
    }

    [HttpPost("batch-download")]
    [SwaggerOperation(OperationId = "StartJavbusBatchDownload")]
    public async Task<BaseResponse> StartBatchDownload([FromBody] JavbusBatchDownloadInputModel model)
    {
        var codes = (model.Codes ?? [])
            .Select(c => c?.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .Select(c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count == 0)
        {
            return BaseResponseBuilder.BuildBadRequest("No code was submitted.");
        }

        var o = options.Value;
        var saveCovers = o.SaveCovers == true;
        if (saveCovers && string.IsNullOrWhiteSpace(o.CoverDirectory))
        {
            return BaseResponseBuilder.BuildBadRequest("Cover saving is enabled but no directory is configured.");
        }

        var settings = new JavbusBatchDownloadSettings
        {
            Concurrency = o.Concurrency ?? DefaultConcurrency,
            DelayMs = o.DelayMs ?? DefaultDelayMs,
            SizeTolerance =
                Math.Clamp(o.SizeTolerancePercentage ?? DefaultSizeTolerancePercentage, 0, 90) / 100m,
            CoverDirectory = saveCovers ? o.CoverDirectory : null
        };

        await btm.Start(BatchDownloadTaskId, () => BTaskBuilder.Create(BatchDownloadTaskId)
            .Named(() => localizer.BTask_Name(BatchDownloadTaskId))
            .Describe(() => localizer.BTask_Description(BatchDownloadTaskId))
            // The batch service keeps one run's table in memory; two at once
            // would overwrite each other.
            .ConflictsWith(BatchDownloadTaskId)
            .Persistent()
            .ReplaceIfExists()
            .WithServiceProvider(HttpContext.RequestServices)
            .Run(async args =>
            {
                await using var scope = args.RootServiceProvider!.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<JavbusBatchDownloadService>();
                await svc.Run(codes, settings, args);
            }));

        return BaseResponseBuilder.Ok;
    }
}
