using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Relocation;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/app/data-path")]
    public class AppDataPathController : Controller
    {
        private readonly AppService _appService;
        private readonly IBOptionsManager<AppOptions> _appOptionsManager;
        private readonly IHubContext<WebGuiHub, IWebGuiClient> _hub;
        private readonly LegacyInstallNoticeState _legacyInstallNoticeState;
        private readonly ILogger<AppDataPathController> _logger;

        public AppDataPathController(
            AppService appService,
            IBOptionsManager<AppOptions> appOptionsManager,
            IHubContext<WebGuiHub, IWebGuiClient> hub,
            LegacyInstallNoticeState legacyInstallNoticeState,
            ILogger<AppDataPathController> logger)
        {
            _appService = appService;
            _appOptionsManager = appOptionsManager;
            _hub = hub;
            _legacyInstallNoticeState = legacyInstallNoticeState;
            _logger = logger;
        }

        public class ValidateRequest
        {
            public string TargetPath { get; set; } = null!;
        }

        public class ValidateResponse
        {
            public bool Valid { get; set; }
            public DataPathValidator.RefusalReason Reason { get; set; }
            public DataPathValidator.TargetState TargetState { get; set; }
            public string? TargetAppVersion { get; set; }
            public long FreeSpaceBytes { get; set; }
            public long MinFreeBytes { get; set; }
            public string CurrentPath { get; set; } = null!;
            public string DefaultPath { get; set; } = null!;
        }

        public class RelocateRequest
        {
            public string TargetPath { get; set; } = null!;
            public RelocationMode Mode { get; set; }
        }

        [HttpPost("validate")]
        [SwaggerOperation(OperationId = "ValidateAppDataPath")]
        public SingletonResponse<ValidateResponse> Validate([FromBody] ValidateRequest req)
        {
            var currentDataDir = NormalisedCurrentDataDir();
            var input = new DataPathValidator.Input
            {
                TargetPath = req.TargetPath,
                CurrentDataDir = currentDataDir,
                Platform = GetCurrentPlatform(),
                CanWrite = DataPathValidator.CanWriteDefault,
                GetFreeSpaceBytes = DataPathValidator.GetFreeSpaceBytesDefault,
                FindVelopackInstallRoot = DataPathValidator.FindVelopackInstallRootDefault,
                // MinFreeBytes left at the default (1 GB heuristic) — see DataPathValidator.
            };

            var result = DataPathValidator.Validate(input);

            return new SingletonResponse<ValidateResponse>(new ValidateResponse
            {
                Valid = result.Valid,
                Reason = result.Reason,
                TargetState = result.State,
                TargetAppVersion = result.TargetAppVersion,
                FreeSpaceBytes = result.FreeSpaceBytes,
                MinFreeBytes = result.MinFreeBytes,
                CurrentPath = currentDataDir,
                DefaultPath = AppService.DefaultAppDataDirectory,
            });
        }

        [HttpPost("relocate")]
        [SwaggerOperation(OperationId = "RelocateAppDataPath")]
        public async Task<BaseResponse> Relocate([FromBody] RelocateRequest req)
        {
            if (AppService.IsEnvironmentDataDirOverride)
            {
                return BaseResponseBuilder.BuildBadRequest(
                    "BAKABASE_DATA_DIR is set; relocation via UI is disabled. " +
                    "Change the environment variable instead.");
            }

            var currentDataDir = NormalisedCurrentDataDir();

            // Defence-in-depth: re-validate server-side.
            var validation = DataPathValidator.Validate(new DataPathValidator.Input
            {
                TargetPath = req.TargetPath,
                CurrentDataDir = currentDataDir,
                Platform = GetCurrentPlatform(),
                CanWrite = DataPathValidator.CanWriteDefault,
                GetFreeSpaceBytes = DataPathValidator.GetFreeSpaceBytesDefault,
                FindVelopackInstallRoot = DataPathValidator.FindVelopackInstallRootDefault,
            });
            if (!validation.Valid)
            {
                return BaseResponseBuilder.BuildBadRequest(
                    $"Validation failed: {validation.Reason}");
            }

            // Mode-specific sanity:
            //   useTarget       requires target to actually have Bakabase data
            //   mergeOverwrite  works for any target state (the runner doesn't care)
            // The overwrite-confirmation phrase is a UX speed-bump enforced in the FE; a
            // direct API caller could bypass it, but there's no actual security boundary it's
            // protecting, so we don't re-validate it here.
            if (req.Mode == RelocationMode.UseTarget &&
                validation.State != DataPathValidator.TargetState.HasBakabaseData)
            {
                return BaseResponseBuilder.BuildBadRequest(
                    "useTarget mode requires the target to already contain Bakabase data.");
            }

            var marker = new PendingRelocation
            {
                Target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(req.TargetPath)),
                Mode = req.Mode,
                CreatedAt = DateTime.UtcNow,
                Language = _appOptionsManager.Value.Language ?? "en-US",
                SourceWhenCreated = currentDataDir,
            };
            marker.WriteTo(currentDataDir);

            _logger.LogInformation(
                "Pending relocation marker written. mode={Mode} target={Target}",
                marker.Mode, marker.Target);

            await _hub.Clients.All.RelocationPending(new
            {
                target = marker.Target,
                mode = marker.Mode.ToString(),
            });

            _appService.NeedRestart = true;
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("relocate")]
        [SwaggerOperation(OperationId = "CancelAppDataPathRelocation")]
        public BaseResponse Cancel()
        {
            PendingRelocation.Delete(NormalisedCurrentDataDir());
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("legacy-notice/dismiss")]
        [SwaggerOperation(OperationId = "DismissLegacyInstallNotice")]
        public async Task<BaseResponse> DismissLegacyNotice()
        {
            await _appOptionsManager.SaveAsync(o =>
                o.LegacyInstallNoticeDismissedAt = DateTime.UtcNow);
            // Clear in-memory state so a subsequent reconnect doesn't replay it before the
            // next launch reads the new dismissed timestamp from app.json.
            _legacyInstallNoticeState.PendingPath = null;
            return BaseResponseBuilder.Ok;
        }

        private string NormalisedCurrentDataDir() =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(_appService.AppDataDirectory));

        private static OSPlatform GetCurrentPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return OSPlatform.Windows;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return OSPlatform.OSX;
            return OSPlatform.Linux;
        }
    }
}
