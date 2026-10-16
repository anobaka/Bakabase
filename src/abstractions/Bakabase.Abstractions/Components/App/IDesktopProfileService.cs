using System.Collections.Generic;
using System.Threading.Tasks;

namespace Bakabase.Abstractions.Components.App;

/// <summary>Desktop-only launch control. A headless server never registers this service.</summary>
public interface IDesktopProfileService
{
    DesktopProfilesView Get();
    DesktopProfileView Add(DesktopProfileAddInput input);
    DesktopProfileView Update(string id, DesktopProfileUpdateInput input);
    void Remove(string id);
    Task<bool> OpenAsync(string id, bool closeCurrent);
    Task CompleteOpenAsync(string id);
}

public sealed record DesktopProfileView(string Id, string Name, string DataDirectory, bool IsCurrent,
    bool IsDefault, bool IsRunning, bool IsAvailable);
public sealed record DesktopProfilesView(bool Available, string? CurrentId, bool EnvironmentOverride,
    string? CurrentDirectory, IReadOnlyList<DesktopProfileView> Profiles);
public sealed record DesktopProfileAddInput(string Name, string Directory, bool Create = false);
public sealed record DesktopProfileUpdateInput(string Name, bool MakeDefault = false);
public sealed record DesktopProfileOpenInput(bool CloseCurrent = false);
