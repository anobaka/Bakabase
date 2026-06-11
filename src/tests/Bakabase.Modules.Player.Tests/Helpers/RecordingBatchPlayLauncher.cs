using Bakabase.Modules.Player.Abstractions.Components;

namespace Bakabase.Modules.Player.Tests.Helpers;

internal sealed class RecordingBatchPlayLauncher : IBatchPlayProcessLauncher
{
    public List<(string ExecutablePath, string Arguments)> Launches { get; } = [];

    public Exception? ThrowOnLaunch { get; set; }

    public Task LaunchAsync(string executablePath, string arguments, CancellationToken ct)
    {
        if (ThrowOnLaunch != null)
        {
            throw ThrowOnLaunch;
        }

        Launches.Add((executablePath, arguments));
        return Task.CompletedTask;
    }
}
