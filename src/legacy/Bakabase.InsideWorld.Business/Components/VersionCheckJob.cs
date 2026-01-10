using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.View;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.BakabaseUpdater;
using Bakabase.InsideWorld.Business.Components.Gui;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Semver;

namespace Bakabase.InsideWorld.Business.Components
{
    /// <summary>
    /// One-time job that checks for new versions 30 seconds after startup
    /// </summary>
    public class VersionCheckJob : BackgroundService
    {
        private readonly ILogger<VersionCheckJob> _logger;
        private readonly AppUpdater _appUpdater;
        private readonly IHubContext<WebGuiHub, IWebGuiClient> _hubContext;
        private readonly BakabaseLocalizer _localizer;

        public VersionCheckJob(
            ILogger<VersionCheckJob> logger,
            AppUpdater appUpdater,
            IHubContext<WebGuiHub, IWebGuiClient> hubContext,
            BakabaseLocalizer localizer)
        {
            _logger = logger;
            _appUpdater = appUpdater;
            _hubContext = hubContext;
            _localizer = localizer;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Wait 30 seconds after startup
                _logger.LogInformation("Version check job scheduled to run in 30 seconds");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Version check job cancelled before execution");
                    return;
                }

                _logger.LogInformation("Starting version check");

                // Get latest version
                var latestVersion = await _appUpdater.CheckNewVersion();

                if (latestVersion == null)
                {
                    _logger.LogInformation("No new version available");
                    return;
                }

                if (!SemVersion.TryParse(latestVersion.Version, SemVersionStyles.Any, out var sv))
                {
                    _logger.LogError("Invalid version format: {Version}", latestVersion.Version);
                    return;
                }

                if (sv.ComparePrecedenceTo(AppService.CoreVersion) <= 0)
                {
                    _logger.LogInformation("Application is up to date");
                    return;
                }

                _logger.LogInformation($"New version available: {latestVersion.Version}, current version: {AppService.CoreVersion}");

                // Send notification to frontend
                var notification = new AppNotificationMessageViewModel
                {
                    Title = _localizer.VersionCheck_NewVersionAvailableTitle(),
                    Message = _localizer.VersionCheck_NewVersionAvailableMessage(latestVersion.Version),
                    Severity = AppNotificationSeverity.Info,
                    Behavior = AppNotificationBehavior.Persistent,
                    Metadata = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "version", latestVersion.Version }
                    }
                };

                await _hubContext.Clients.All.OnNotification(notification);
                _logger.LogInformation("Version update notification sent to frontend");
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while checking for version updates");
            }
        }
    }
}
