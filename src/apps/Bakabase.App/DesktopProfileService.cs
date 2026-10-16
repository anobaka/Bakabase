using Bakabase.Abstractions.Components.App;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Profiles;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Service.Components.ServerData;

namespace Bakabase.App;

internal sealed class DesktopProfileService : IDesktopProfileService
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
        if (View(profile, "").IsRunning) throw new InvalidOperationException("Close this workspace before removing its registration. Its data will remain untouched.");
        _registry.Remove(id, DesktopProfileSelection.CurrentId);
    }
    public async Task<bool> OpenAsync(string id, bool closeCurrent)
    {
        var profile = _registry.Get(id);
        var view = View(profile, _registry.Read().DefaultId);
        if (!view.IsAvailable) throw new DirectoryNotFoundException("Reconnect this workspace's storage before opening it.");
        if (view.IsCurrent)
        {
            await DesktopSetupBootstrap.TryActivateExistingAsync([], profile.AnchorDirectory);
            return false;
        }
        // The historical closeCurrent field no longer permits multiple workspaces.
        // Prepare now; commit only after the controller has flushed its HTTP response.
        var parent = SetupChildConnection.Current ?? throw new InvalidOperationException("The desktop workspace coordinator is unavailable.");
        await parent.RequestWorkspaceSwitchAsync(id, commit: false);
        return true;
    }

    public async Task CompleteOpenAsync(string id)
    {
        if (SetupChildConnection.Current is { } parent)
            await parent.RequestWorkspaceSwitchAsync(id, commit: true);
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
