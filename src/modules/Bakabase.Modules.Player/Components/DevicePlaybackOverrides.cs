using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Services;

namespace Bakabase.Modules.Player.Components;

public static class DevicePlaybackOverrides
{
    public static string KindForPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (InternalOptions.ImageExtensions.Contains(ext)) return "image";
        if (InternalOptions.AudioExtensions.Contains(ext)) return "audio";
        if (InternalOptions.VideoExtensions.Contains(ext)) return "video";
        if (InternalOptions.TextExtensions.Contains(ext)) return "text";
        return "other";
    }

    public static async Task<DevicePlaybackOverrideSnapshot> SnapshotAsync(DevicePlaybackOverridesStore store,
        IPlayerExecutableLocator locator, string target, CancellationToken ct)
    {
        var rules = await store.ReadAsync(target, ct);
        var openers = KnownPlayerDefinitions.All.Select(p => new DevicePlaybackOpener(p.Id, p.DisplayName,
            locator.Locate(p).Count > 0, p.SupportsHttp,
            p.SupportedExtensions == null ? DevicePlaybackOverride.Kinds : p.SupportedExtensions
                .Select(extension => KindForPath("file" + extension)).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray())).ToArray();
        return new(rules, openers);
    }
}
