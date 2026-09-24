using System.Net;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Infrastructures.Components.App;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// Runs a downloaded DLsite work on this machine.
/// </summary>
/// <remarks>
/// <para>
/// The one action in this set that cannot fall back to streaming. Everything else the
/// client plays can come down a URL, but a program has to be executed from real files:
/// its data directory, its DLLs, whatever it writes as it runs. So an unmapped work is
/// refused, and the refusal says what would fix it.
/// </para>
/// <para>
/// Which file to run stays the server's answer — it needs the work's type, its download
/// location and the priority rules over that folder. What is decided here is only what
/// this machine can do about it.
/// </para>
/// </remarks>
public sealed class DLsiteLaunchHandler(
    ActiveConnection connection,
    IUpstreamApi upstream,
    IShellOpener shell,
    ILocaleEmulatorLauncher localeEmulator,
    ILogger<DLsiteLaunchHandler> logger) : PathHandlerBase(connection)
{
    public override string RouteKey => "POST /dlsite-work/{workId}/launch";

    public override async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        if (!routeValues.TryGetValue("workId", out var workId) || string.IsNullOrWhiteSpace(workId))
        {
            await WriteAsync(context, HttpStatusCode.BadRequest, "No work id was given.");
            return;
        }

        var target = await upstream.GetDLsiteWorkLaunchTargetAsync(workId, context.RequestAborted);

        if (target == null)
        {
            await WriteAsync(context, HttpStatusCode.ServiceUnavailable,
                $"Could not ask the server what to run for {workId}. It may not be downloaded yet, " +
                "or the server may be unreachable.");

            return;
        }

        var localPath = await MapOrRefuseAsync(context, target.File);

        if (localPath == null)
        {
            return;
        }

        if (!File.Exists(localPath))
        {
            await WriteAsync(context, HttpStatusCode.NotFound,
                $"'{localPath}' does not exist on this machine. A downloaded work has to be reachable here " +
                "to run — streaming it is not an option for a program. Check the path mapping for the " +
                "folder the work was downloaded to.");

            return;
        }

        try
        {
            await LaunchAsync(target, localPath, context.RequestAborted);
            await WriteAsync(context, HttpStatusCode.OK, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to launch {Path}", localPath);
            await WriteAsync(context, HttpStatusCode.InternalServerError,
                $"Could not run '{localPath}' on your machine: {e.Message}");
        }
    }

    private async Task LaunchAsync(DLsiteWorkLaunchTarget target, string localPath, CancellationToken ct)
    {
        // A document opens with whatever this machine associates with it; only a program
        // can be locale-emulated, and only where Locale Emulator is actually installed.
        if (target is {IsExecutable: true, UseLocaleEmulator: true} && localeEmulator.IsAvailable)
        {
            try
            {
                await localeEmulator.LaunchAsync(localPath, ct);
                return;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to launch with Locale Emulator, falling back to direct launch");
            }
        }

        shell.LaunchProgram(localPath, target.IsExecutable ? Path.GetDirectoryName(localPath) : null);
    }
}

/// <summary>
/// Runs a program under Locale Emulator on this machine, if it is there.
/// </summary>
/// <remarks>
/// The client has no installer for it — downloading and unpacking components is the
/// server's job and stays there. What this does is use an installation that already
/// exists, in the same place under this client's data directory the server keeps its own.
/// A user who has one gets Japanese locale; one who does not gets the direct launch,
/// which is what the server does when the component is missing too.
/// </remarks>
public interface ILocaleEmulatorLauncher
{
    bool IsAvailable { get; }

    Task LaunchAsync(string executablePath, CancellationToken ct);
}

public sealed class LocaleEmulatorLauncher(IServiceProvider services, IShellOpener shell) : ILocaleEmulatorLauncher
{
    /// <summary>
    /// Mirrors the server's component layout: components/locale-emulator/LEProc.exe.
    /// </summary>
    /// <remarks>
    /// <see cref="AppService"/> is resolved on use rather than injected, so building the
    /// pipeline does not require one — a test host has no application data directory and
    /// no business having one forced on it just to enumerate handlers.
    /// </remarks>
    private string? ExecutablePath =>
        services.GetService(typeof(AppService)) is AppService appService
            ? Path.Combine(appService.ComponentsPath, "locale-emulator", "LEProc.exe")
            : null;

    public bool IsAvailable => OperatingSystem.IsWindows() && ExecutablePath is {} path && File.Exists(path);

    public Task LaunchAsync(string executablePath, CancellationToken ct)
    {
        var leProc = ExecutablePath ??
                     throw new InvalidOperationException("Locale Emulator is not installed for this client.");

        // -run is what LEProc takes; the server passes the same.
        shell.LaunchProcess(leProc, $"-run \"{executablePath}\"", useShellExecute: false);

        return Task.CompletedTask;
    }
}
