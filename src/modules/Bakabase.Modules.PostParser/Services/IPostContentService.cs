using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>Reads a link or pasted text using installed platform readers, without creating a task.</summary>
public interface IPostContentService
{
    bool CanRead(string reference, string? sourceHint = null);

    /// <param name="sourceHint">Optional platform name for legacy platform-specific references.</param>
    Task<PostContent> ReadAsync(string reference, string? sourceHint = null, CancellationToken ct = default);
}
