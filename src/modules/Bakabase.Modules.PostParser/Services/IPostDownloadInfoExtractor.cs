using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>Extracts links, access codes and archive passwords; it does not download their content.</summary>
public interface IPostDownloadInfoExtractor
{
    Task<PostDownloadInfo> ExtractAsync(PostContent content, CancellationToken ct = default);
}
