using System.Diagnostics;
using Bakabase.Abstractions.Components.App;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Profiles;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Microsoft.Extensions.Hosting;

namespace Bakabase.App;

internal sealed class DesktopProfileService(IHostApplicationLifetime lifetime) : IDesktopProfileService
{
    private readonly DesktopProfileRegistry _registry = DesktopProfileRegistry.ForCurrentUser();
    private string CurrentDirectory => AppDataLocator.ResolveEffectiveDataDirectory();
    public DesktopProfilesView Get()
    {
        var state = _registry.Read();
        return new(true, DesktopProfileSelection.CurrentId, AppDataLocator.IsEnvironmentOverride,
            CurrentDirectory, state.Profiles.Select(p => View(p, state.DefaultId)).ToArray());
    }
    public DesktopProfileView Add(DesktopProfileAddInput input)
    {
        var added = _registry.Add(input.Name, input.Directory, input.Create);
        return View(added, _registry.Read().DefaultId);
    }
    public DesktopProfileView Update(string id, DesktopProfileUpdateInput input)
    {
        var updated = _registry.Update(id, input.Name, input.MakeDefault);
        return View(updated, _registry.Read().DefaultId);
    }
    public void Remove(string id)
    {
        var profile = _registry.Get(id);
        if (View(profile, "").IsRunning) throw new InvalidOperationException("Close this data space before removing its registration. Its data will remain untouched.");
        _registry.Remove(id, DesktopProfileSelection.CurrentId);
    }
    public async Task OpenAsync(string id, bool closeCurrent)
    {
        var profile = _registry.Get(id);
        var view = View(profile, _registry.Read().DefaultId);
        if (!view.IsAvailable) throw new DirectoryNotFoundException("Reconnect this data space's storage before opening it.");
        // Registration of the active ENV directory must activate it, not launch a second owner.
        if (view.IsCurrent)
        {
            await DesktopSetupBootstrap.TryActivateExistingAsync([], profile.AnchorDirectory);
            return;
        }
        if (!await DesktopSetupBootstrap.TryActivateExistingAsync([], profile.AnchorDirectory))
        {
            using var launched = Process.Start(DesktopProfileSelection.CreateStartInfo(id))
                ?? throw new InvalidOperationException("Could not start the selected data space.");
            // Do not close the current space until the new coordinator owns its activation
            // channel. Setup may still be waiting for user choices; that is a valid open.
            var ready = false;
            for (var i = 0; i < 40; i++)
            {
                if (launched.HasExited) throw new InvalidOperationException("The selected data space could not start. The current space remains open.");
                if (await DesktopSetupBootstrap.TryActivateExistingAsync([], profile.AnchorDirectory)) { ready = true; break; }
                await Task.Delay(250);
            }
            if (!ready) throw new InvalidOperationException("The selected data space is still starting. The current space remains open.");
        }
        if (closeCurrent)
            _ = Task.Run(async () => { await Task.Delay(500); lifetime.StopApplication(); });
    }
    private DesktopProfileView View(DesktopProfile profile, string defaultId)
    {
        var data = AppDataLocator.ResolveEffectiveDataDirectory(profile.AnchorDirectory);
        var current = DataDirectoryIdentity.Normalize(data) == DataDirectoryIdentity.Normalize(CurrentDirectory);
        return new(profile.Id, profile.Name, data, current, profile.Id == defaultId,
            current || IsLocked(Path.Combine(profile.AnchorDirectory, Service.Components.ServerData.SetupProcessCoordinator.LockFileName)) ||
            IsLocked(Path.Combine(data, DataDirectoryLock.FileName)),
            Directory.Exists(data) || profile.RequiresSetup);
    }
    private static bool IsLocked(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
