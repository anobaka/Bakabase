using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.ResponseModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/app")]
    public class AppController : Controller
    {
        private readonly AppService _appService;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<AppController> _logger;

        public AppController(
            AppService appService,
            IHostApplicationLifetime lifetime,
            ILogger<AppController> logger)
        {
            _appService = appService;
            _lifetime = lifetime;
            _logger = logger;
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
