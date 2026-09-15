using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Platform;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
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
public class SteamConnector(ISteamAppService apps, ILogger<SteamConnector> logger,
    IPlatformClientLauncher launcher) : IPlatformConnector
{
    public ResourceSource Source => ResourceSource.Steam;

    public bool CanFetch => true;

    public async Task<IReadOnlyList<PlatformFetchValidationIssue>> ValidateFetchAsync(string sourceKey,
        CancellationToken ct)
    {
        if (!int.TryParse(sourceKey, out var appId) || appId <= 0)
            return [new("acquisition.steam.invalidAppId", "Use a positive Steam app ID.",
                "workflow.validation.acquisition.steamInvalidAppId")];
        var app = await apps.GetByAppId(appId);
        if (app == null)
            return [new("acquisition.steam.notInLibrary", "Sync your Steam library before acquiring this game.",
                "workflow.validation.acquisition.steamNotInLibrary")];
        if (!string.IsNullOrWhiteSpace(app.InstallPath) && Directory.Exists(app.InstallPath)) return [];
        if (!launcher.IsAvailable)
            return [new("acquisition.steam.clientUnavailable", "This server cannot open the Steam client. It can only associate an existing installation directory that the server can access.",
                "workflow.validation.acquisition.steamClientUnavailable")];
        return [];
    }

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
        if (!int.TryParse(sourceKey, out var appId) || appId <= 0)
        {
            return new PlatformFetchOutcome.Refused($"\"{sourceKey}\" is not a Steam app id.");
        }

        if (await DetectLocalPathAsync(sourceKey, ct) is { } existing)
            return new PlatformFetchOutcome.Done(existing);

        var app = await apps.GetByAppId(appId);

        if (app == null)
        {
            return new PlatformFetchOutcome.Refused(
                $"App {appId} is not in your Steam library. Sync it first.");
        }

        if (!launcher.IsAvailable)
            return new PlatformFetchOutcome.Refused(
                "This server cannot open Steam. Associate an existing installation directory that the server can access.");

        try
        {
            // Steam's own protocol handler: it opens the client on the install dialog, which is
            // as far as anything outside Steam can take this.
            launcher.Open($"steam://install/{appId}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Steam] Could not open the install link for {AppId}", appId);

            return new PlatformFetchOutcome.Refused(
                $"Could not open Steam. Install {app.Name ?? appId.ToString()} yourself and this will notice.");
        }

        return new PlatformFetchOutcome.Started(
            $"The Steam install dialog was requested for {app.Name ?? appId.ToString()}. Confirm the installation in Steam; this will continue when its files are available.");
    }

    public async Task<string?> DetectLocalPathAsync(string sourceKey, CancellationToken ct)
    {
        if (!int.TryParse(sourceKey, out var appId) || appId <= 0) return null;
        var app = await apps.GetByAppId(appId);
        // A mapped library may be accessible to a server without a local Steam installation.
        // Preserve that path instead of clearing it through a desktop-only library scan.
        if (!string.IsNullOrWhiteSpace(app?.InstallPath) && Directory.Exists(app.InstallPath))
            return app.InstallPath;
        if (!launcher.IsAvailable) return null;
        await apps.UpdateInstallationStatus();
        app = await apps.GetByAppId(appId);
        return !string.IsNullOrWhiteSpace(app?.InstallPath) && Directory.Exists(app.InstallPath)
            ? app.InstallPath : null;
    }
}
