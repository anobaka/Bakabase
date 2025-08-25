using Bakabase.Abstractions.Components;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Components.Cryptography;

namespace Bakabase.Abstractions.Models.Domain;


public record PathPropertyExtractor: ISyncVersion
{
    public PathPropertyExtractorBasePathType BasePathType { get; set; } = PathPropertyExtractorBasePathType.MediaLibrary;
    public PathPositioner Positioner { get; set; }
    public int? Layer { get; set; }
    public string? Regex { get; set; }
    public string GetSyncVersion()
    {
        return CryptographyUtils.Md5($"{BasePathType}-{Positioner}-{Layer}-{Regex}").Substring(7);
    }
}
