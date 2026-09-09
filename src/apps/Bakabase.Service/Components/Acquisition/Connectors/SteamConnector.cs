using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Platform;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.App;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Connectors;

/// <summary>
/// Steam, as a place the user owns games on.
/// <para>
/// Fetching means installing, and installing means handing the job to Steam and waiting: it owns
/// the download, the disk layout and the progress bar, and nothing here can or should reproduce
/// any of that. What this does is ask, and then notice when the files exist.
/// </para>
/// </summary>
public class SteamConnector(ISteamAppService apps, ILogger<SteamConnector> logger) : IPlatformConnector
{
    public ResourceSource Source => ResourceSource.Steam;

    public bool CanFetch => true;

    public async Task<IReadOnlyList<PlatformHolding>> EnumerateHoldingsAsync(CancellationToken ct) =>
        (await apps.GetAll())
        .Select(a => new PlatformHolding(
            a.AppId.ToString(),
            a.Name ?? a.AppId.ToString(),
            a.IsInstalled ? a.InstallPath : null,
            a.ImgIconUrl == null ? null : [a.ImgIconUrl]))
        .ToList();

    public async Task<PlatformFetchOutcome> FetchAsync(string sourceKey, string workDirectory,
        Func<int, string?, Task>? onProgress, CancellationToken ct)
    {
        if (!int.TryParse(sourceKey, out var appId))
        {
            return new PlatformFetchOutcome.Refused($"\"{sourceKey}\" is not a Steam app id.");
        }

        // Steam may have installed it since the last sync; ask before asking the user.
        await apps.UpdateInstallationStatus();

        var app = await apps.GetByAppId(appId);

        if (app == null)
        {
            return new PlatformFetchOutcome.Refused(
                $"App {appId} is not in your Steam library. Sync it first.");
        }

        if (app is {IsInstalled: true, InstallPath: not null} && System.IO.Directory.Exists(app.InstallPath))
        {
            return new PlatformFetchOutcome.Done(app.InstallPath);
        }

        try
        {
            // Steam's own protocol handler: it opens the client on the install dialog, which is
            // as far as anything outside Steam can take this.
            OsShell.Open($"steam://install/{appId}", false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Steam] Could not open the install link for {AppId}", appId);

            return new PlatformFetchOutcome.Refused(
                $"Could not open Steam. Install {app.Name ?? appId.ToString()} yourself and this will notice.");
        }

        return new PlatformFetchOutcome.Started(
            $"Steam is installing {app.Name ?? appId.ToString()}. This will carry on when it is there.");
    }

    public async Task<string?> DetectLocalPathAsync(string sourceKey, CancellationToken ct)
    {
        if (!int.TryParse(sourceKey, out var appId)) return null;

        await apps.UpdateInstallationStatus();

        var app = await apps.GetByAppId(appId);

        return app is {IsInstalled: true, InstallPath: not null} ? app.InstallPath : null;
    }
}
