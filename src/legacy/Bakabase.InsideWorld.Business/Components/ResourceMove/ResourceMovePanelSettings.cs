using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.ResourceMove;
using Bakabase.Abstractions.Extensions;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.ResourceMove;

/// <summary>Serializes configuration writes, including policy changes from conflict resolution.</summary>
public class ResourceMovePanelSettings(IBOptionsManager<ResourceMovePanelOptions> options)
    : IResourceMovePanelSettings
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public bool AutoOverwrite => options.Value.AutoOverwrite;

    public ResourceMovePanelOptions Get() => new()
    {
        Revision = options.Value.Revision,
        AutoOverwrite = options.Value.AutoOverwrite,
        Destinations = options.Value.Destinations.Select(d => d with { }).ToList()
    };

    public async Task SetAutoOverwrite(bool enabled)
    {
        await _gate.WaitAsync();
        try
        {
            await options.SaveAsync(o =>
            {
                o.AutoOverwrite = enabled;
                o.Revision++;
            });
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> Save(ResourceMovePanelOptions input)
    {
        await _gate.WaitAsync();
        try
        {
            if (input.Revision != options.Value.Revision) return false;
            var destinations = Normalize(input.Destinations);
            await options.SaveAsync(o =>
            {
                o.Destinations = destinations;
                o.AutoOverwrite = input.AutoOverwrite;
                o.Revision++;
            });
            return true;
        }
        finally { _gate.Release(); }
    }

    public static List<ResourceMoveDestination> Normalize(IEnumerable<ResourceMoveDestination> destinations)
    {
        var input = destinations.ToList();
        if (input.Count > 1000) throw new ArgumentException("Too many move destinations.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in input)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
                throw new ArgumentException("Move destination IDs must be unique.");
            if (item.Scope != "global" && item.Scope != "tab")
                throw new ArgumentException("Invalid move destination scope.");
            if (item.Scope == "tab" && string.IsNullOrWhiteSpace(item.TabId))
                throw new ArgumentException("A tab destination requires a tab ID.");
            if (string.IsNullOrWhiteSpace(item.Path) || !Path.IsPathFullyQualified(item.Path))
                throw new ArgumentException("An absolute destination path is required.");
        }

        // Do not reject offline destinations while saving: losing a mount must not erase
        // bookmarks, and removing one must not require the directory to exist.
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var globalPaths = input.Where(d => !d.IsDeleted && d.Scope == "global")
            .Select(d => d.Path.StandardizePath()!).ToHashSet(comparer);
        var seen = new HashSet<string>(comparer);
        return input.Select(d => d with
        {
            Path = d.Path.StandardizePath()!,
            Name = string.IsNullOrWhiteSpace(d.Name) ? null : d.Name.Trim(),
            TabId = d.Scope == "global" ? null : d.TabId,
            IsDeleted = d.IsDeleted || (d.Scope == "tab" && globalPaths.Contains(d.Path.StandardizePath()!))
        }).Select(d => d with
        {
            IsDeleted = d.IsDeleted || !seen.Add($"{d.Scope}\0{d.TabId}\0{d.Path}")
        }).ToList();
    }
}
