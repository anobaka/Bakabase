using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.ResponseModels;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Service.Components;
using Bakabase.Service.Models.View;
using Bakabase.Service.Services;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/app")]
    public class AppController : Controller
    {
        private readonly AppService _appService;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<AppController> _logger;
        private readonly IDeviceIdService _deviceIdService;
        private readonly IBOptions<AppOptions> _appOptions;
        private readonly IOptions<AnalyticsConfiguration> _analyticsConfig;
        private readonly IWebHostEnvironment _env;
        private readonly ITelemetrySnapshotService _telemetrySnapshotService;

        public AppController(
            AppService appService,
            IHostApplicationLifetime lifetime,
            ILogger<AppController> logger,
            IDeviceIdService deviceIdService,
            IBOptions<AppOptions> appOptions,
            IOptions<AnalyticsConfiguration> analyticsConfig,
            IWebHostEnvironment env,
            ITelemetrySnapshotService telemetrySnapshotService)
        {
            _appService = appService;
            _lifetime = lifetime;
            _logger = logger;
            _deviceIdService = deviceIdService;
            _appOptions = appOptions;
            _analyticsConfig = analyticsConfig;
            _env = env;
            _telemetrySnapshotService = telemetrySnapshotService;
        }

        [HttpGet("initialized")]
        [SwaggerOperation(OperationId = "CheckAppInitialized")]
        public async Task<SingletonResponse<InitializationContentType>> Initialized()
        {
            if (_appService.NotAcceptTerms)
            {
                return new SingletonResponse<InitializationContentType>(InitializationContentType.NotAcceptTerms);
            }

            if (_appService.NeedRestart)
            {
                return new SingletonResponse<InitializationContentType>(InitializationContentType.NeedRestart);
            }

            return SingletonResponseBuilder<InitializationContentType>.Ok;
        }

        [HttpGet("info")]
        [SwaggerOperation(OperationId = "GetAppInfo")]
        public async Task<SingletonResponse<AppInfo>> Info()
        {
            return new SingletonResponse<AppInfo>(_appService.AppInfo);
        }

        /// <summary>
        /// Bootstrapping payload for the frontend analytics layer (Clarity / GA4 / Sentry).
        /// Returns the persisted anonymous device id, the user-controlled tracking flag, and the
        /// configured project ids / DSN. Per-app statistics live on the separate telemetry
        /// snapshot endpoint.
        /// </summary>
        [HttpGet("analytics-info")]
        [SwaggerOperation(OperationId = "GetAnalyticsAppInfo")]
        public SingletonResponse<AnalyticsAppInfoViewModel> AnalyticsInfo()
        {
            var cfg = _analyticsConfig.Value;
            var vm = new AnalyticsAppInfoViewModel
            {
                EnableAnonymousDataTracking = _appOptions.Value.EnableAnonymousDataTracking,
                DeviceId = _deviceIdService.GetOrCreate(),
                ReleaseChannel = ReleaseChannelDetector.Detect(_env),
                ClarityProjectId = string.IsNullOrWhiteSpace(cfg.Clarity.ProjectId) ? null : cfg.Clarity.ProjectId,
                Ga4MeasurementId = string.IsNullOrWhiteSpace(cfg.Ga4.MeasurementId) ? null : cfg.Ga4.MeasurementId,
                SentryDsn = string.IsNullOrWhiteSpace(cfg.Sentry.FrontendDsn) ? null : cfg.Sentry.FrontendDsn,
            };
            return new SingletonResponse<AnalyticsAppInfoViewModel>(vm);
        }

        /// <summary>
        /// Generates a fresh anonymous device id. Used by the configuration page's
        /// "reset anonymous id" UX so users can break the linkage to their historical data.
        /// </summary>
        [HttpPost("analytics-info/reset-device-id")]
        [SwaggerOperation(OperationId = "ResetAnalyticsDeviceId")]
        public SingletonResponse<string> ResetDeviceId()
        {
            var id = _deviceIdService.Reset();
            return new SingletonResponse<string>(id);
        }

        /// <summary>
        /// Per-install state used to populate GA4 user properties + event metrics. Counts
        /// only — never paths, names, or PII. Frontend hashes the response and uploads only
        /// when changed (see design §6.5).
        /// </summary>
        [HttpGet("telemetry-snapshot")]
        [SwaggerOperation(OperationId = "GetAppTelemetrySnapshot")]
        public async Task<SingletonResponse<TelemetrySnapshotViewModel>> TelemetrySnapshot()
        {
            var snapshot = await _telemetrySnapshotService.GenerateAsync();
            return new SingletonResponse<TelemetrySnapshotViewModel>(snapshot);
        }

        [HttpPost("terms")]
        [SwaggerOperation(OperationId = "AcceptTerms")]
        public async Task<BaseResponse> AcceptTerms()
        {
            _appService.NotAcceptTerms = false;
            return BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// Plain-restart endpoint: spawns a fresh copy of the current EXE and asks the host to
        /// stop. Used by the relocation flow (and any future "restart please" UX) so we don't
        /// hijack the Velopack updater for a non-update restart.
        ///
        /// Dev / Visual Studio caveat: when running under a debugger, the spawned child shares
        /// the parent's console and VS keeps the parent alive — so the user sees the parent
        /// linger. We can't fix that from the server; the FE is expected to message this for
        /// debug builds.
        /// </summary>
        [HttpPost("restart")]
        [SwaggerOperation(OperationId = "RestartApp")]
        public BaseResponse Restart()
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                _logger.LogError("Cannot determine current process path; cannot restart.");
                return BaseResponseBuilder.BuildBadRequest("Cannot determine current process path.");
            }

            // Fire-and-forget so the HTTP response gets back to the FE first; then we spawn
            // and ask the host to stop. The brief delay is intentional — we want the response
            // headers flushed before the host starts tearing down sockets.
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500);
                    _logger.LogInformation("Restarting via spawn-then-stop. exe={Exe}", exePath);

                    var psi = new ProcessStartInfo(exePath)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = System.IO.Path.GetDirectoryName(exePath) ?? string.Empty,
                    };
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to spawn replacement process; will still stop the host.");
                }
                finally
                {
                    _lifetime.StopApplication();
                }
            });

            return BaseResponseBuilder.Ok;
        }
    }
}
