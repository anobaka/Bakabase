using Bakabase.Abstractions.Components;
using Bootstrap.Components.Cryptography;
using Bootstrap.Extensions;

namespace Bakabase.Abstractions.Models.Domain;

public record MediaLibraryTemplate : ISyncVersion
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Author { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<PathFilter>? ResourceFilters { get; set; }
    public List<MediaLibraryTemplateProperty>? Properties { get; set; }
    public MediaLibraryTemplatePlayableFileLocator? PlayableFileLocator { get; set; }
    public List<MediaLibraryTemplateEnhancerOptions>? Enhancers { get; set; }
    public string? DisplayNameTemplate { get; set; }
    public List<string>? SamplePaths { get; set; }
    public int? ChildTemplateId { get; set; }
    public MediaLibraryTemplate? Child { get; set; }

    public string GetSyncVersion()
    {
        var versions = new List<string>();
        if (ResourceFilters != null)
        {
            foreach (var rf in ResourceFilters)
            {
                var rfv = rf.GetSyncVersion();
                if (rfv.IsNotEmpty())
                {
                    versions.Add(rfv);
                }
            }
        }

        if (Properties != null)
        {
            foreach (var p in Properties.OrderBy(d => d.Pool).ThenBy(d => d.Id))
            {
                var pv = p.GetSyncVersion();
                if (pv.IsNotEmpty())
                {
                    versions.Add(pv);
                }
            }
        }

        versions.Add(Child?.GetSyncVersion() ?? "");

        return CryptographyUtils.Md5(string.Join(',', versions)).Substring(7);
    }
}