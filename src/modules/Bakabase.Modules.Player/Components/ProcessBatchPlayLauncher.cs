using System.Diagnostics;
using Bakabase.Modules.Player.Abstractions.Components;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Player.Components;

public class ProcessBatchPlayLauncher(ILogger<ProcessBatchPlayLauncher> logger) : IBatchPlayProcessLauncher
{
    public Task LaunchAsync(string executablePath, string arguments, CancellationToken ct)
    {
        // .lnk shortcuts are only launchable through the shell; a direct
        // CreateProcess on them fails with "not a valid Win32 application".
        var useShellExecute = Path.GetExtension(executablePath)
            .Equals(".lnk", StringComparison.OrdinalIgnoreCase);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo(executablePath, arguments)
            {
                UseShellExecute = useShellExecute,
            },
        };

        // Start synchronously so an unlaunchable player surfaces as an API
        // error instead of dying silently in a background task; do not wait
        // for the player itself to exit.
        process.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Waiting for player process '{Executable}' failed", executablePath);
            }
            finally
            {
                process.Dispose();
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }
}
