namespace Bakabase.Modules.RemoteAccess.Abstractions.Models;

public static class ManagedResourceUploadLimits
{
    public const int MaximumFileBytes = 16 * 1024 * 1024;
    public const int MaximumEnvelopeBytes = (MaximumFileBytes + 2) / 3 * 4 + 65536;

    public static bool IsOwnerUploadPath(string pathAndQuery)
    {
        var path = pathAndQuery.Split('?')[0];
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 5 && parts[0] == "management" && parts[1] == "library" &&
               parts[2] == "resources" && int.TryParse(parts[3], out var id) && id > 0 &&
               parts[4] is "attachment" or "torrent";
    }
}
