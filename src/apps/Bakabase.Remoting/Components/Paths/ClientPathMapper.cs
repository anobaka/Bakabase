using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Management;

namespace Bakabase.Remoting.Components.Paths;

public sealed record PathMappingResult(string? LocalPath, string? UnmappedServerPath)
{
    public bool Mapped => LocalPath != null;
    public static PathMappingResult Ok(string localPath) => new(localPath, null);
    public static PathMappingResult NotMapped(string? serverPath) => new(null, serverPath);
}

/// <summary>Desktop compatibility adapter to the common directional mapper used by headless workers.</summary>
public static class ClientPathMapper
{
    public static PathMappingResult Map(string? serverPath, IEnumerable<ClientPathMapping> mappings)
    {
        var mapped = ManagedPathMapper.Map(serverPath, mappings.Select(m => new ManagedServerPathMapping(m.ServerPath, m.LocalPath)));
        return new(mapped.LocalPath, mapped.UnmappedServerPath);
    }
}
