using Bakabase.Modules.Player.Abstractions.Models.Domain;

namespace Bakabase.Modules.Player.Abstractions.Services;

public interface IDevicePlaybackPreferencesStore
{
    Task<DevicePlaybackPreferences> ReadAsync(CancellationToken ct = default);

    Task WriteAsync(DevicePlaybackPreferences preferences, CancellationToken ct = default);
}
