using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakabase.InsideWorld.Models.Configs;

/// <summary>
/// Install-wide navigation preferences. Routes and capability rules remain in the UI's
/// catalog; this stores only order, grouping and visibility, including unknown catalog IDs
/// so a temporary downgrade does not discard preferences from a newer build.
/// </summary>
public class UINavigationLayout
{
    public int Version { get; set; } = 1;
    public List<UINavigationNode> Items { get; set; } = [];
    public List<string> KnownCatalogIds { get; set; } = [];
    public Dictionary<string, string> DirectoryAliases { get; set; } = [];

    public string? Validate()
    {
        if (Version != 1) return "Unsupported navigation layout version.";
        if (Items == null || KnownCatalogIds == null || DirectoryAliases == null)
            return "Navigation layout collections are required.";
        if (KnownCatalogIds.Count > 2048 || DirectoryAliases.Count > 512)
            return "Navigation layout contains too many catalog entries.";
        if (KnownCatalogIds.Any(id => !ValidId(id)) ||
            DirectoryAliases.Any(pair => !ValidId(pair.Key) || !ValidId(pair.Value)))
            return "Navigation layout contains an invalid catalog ID.";

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        string? Check(IEnumerable<UINavigationNode> nodes, bool insideDirectory)
        {
            foreach (var node in nodes)
            {
                if (++count > 1024) return "Navigation layout contains too many items.";
                if (node == null || !ValidId(node.Id) || !ids.Add(node.Id))
                    return "Navigation layout contains an invalid or duplicate item ID.";
                if (node.Kind is not ("menu" or "directory"))
                    return "Navigation items must be menus or directories.";
                if (node.Kind == "directory")
                {
                    if (insideDirectory) return "Menu directories must be at the first level.";
                    if (node.Name != null && (string.IsNullOrWhiteSpace(node.Name) ||
                        node.Name.Length > 80 || node.Name.Any(char.IsControl)))
                        return "Menu directory names must contain 1 to 80 characters.";
                    var error = Check(node.Children ?? [], true);
                    if (error != null) return error;
                }
                else if (node.Name != null || node.Children?.Count > 0)
                {
                    return "Menus cannot be renamed or contain other items.";
                }
            }
            return null;
        }
        return Check(Items, false);
    }

    private static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id) &&
        id.Length <= 192 && !id.Any(char.IsControl);
}

public class UINavigationNode
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "menu";
    /// <summary>Only directory labels can be overridden.</summary>
    public string? Name { get; set; }
    public bool Hidden { get; set; }
    public List<UINavigationNode>? Children { get; set; }
}
