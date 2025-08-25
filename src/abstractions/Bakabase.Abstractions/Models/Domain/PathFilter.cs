using Bakabase.Abstractions.Components;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Components.Cryptography;

namespace Bakabase.Abstractions.Models.Domain;

public record PathFilter : ISyncVersion
{
    public PathPositioner Positioner { get; set; }
    public int? Layer { get; set; }
    public string? Regex { get; set; }
    public PathFilterFsType? FsType { get; set; }
    public HashSet<int>? ExtensionGroupIds { get; set; }
    public List<ExtensionGroup>? ExtensionGroups { get; set; }
    public HashSet<string>? Extensions { get; set; }

    public string? GetSyncVersion()
    {
        var extensions = new HashSet<string>();
        if (ExtensionGroups != null)
        {
            foreach (var eg in ExtensionGroups)
            {
                if (eg.Extensions != null)
                {
                    extensions.UnionWith(eg.Extensions);
                }
            }
        }

        if (Extensions != null)
        {
            extensions.UnionWith(Extensions);
        }

        return CryptographyUtils.Md5(
            $"{Positioner}-{Layer}-{Regex}-{FsType}-{string.Join(',', extensions.OrderBy(e => e))}").Substring(7);
    }
}