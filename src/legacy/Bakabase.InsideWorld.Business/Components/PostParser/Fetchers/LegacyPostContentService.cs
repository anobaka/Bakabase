using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using LegacyPostContent = Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.PostContent;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;

/// <summary>Adapts existing platform readers to the task-independent post parsing capability.</summary>
public class LegacyPostContentService(IEnumerable<ISharedContentReader> readers) : IPostContentService
{
    private readonly List<ISharedContentReader> _readers = readers.OrderByDescending(r => r.Priority).ToList();

    public bool CanRead(string reference, string? sourceHint = null) => Resolve(reference, sourceHint) != null;

    public async Task<PostContent> ReadAsync(string reference, string? sourceHint = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var reader = Resolve(reference, sourceHint)
                     ?? throw new NotSupportedException("No installed post reader supports this link or text.");
        var content = await reader.ReadAsync(reference.Trim(), ct);
        return ToContent(content, reader.Source?.ToString());
    }

    public static PostContent ToContent(LegacyPostContent content, string? sourceHint = null) => new()
    {
        Title = content.Title ?? "",
        MainHtml = content.MainHtml ?? "",
        CommentHtmlList = content.CommentHtmlList?.ToList() ?? [],
        Locks = content.Locks?.Select(l => new PostContentLock(l.Url, l.Price, l.IsBought)).ToList() ?? [],
        SourceHint = sourceHint
    };

    private ISharedContentReader? Resolve(string reference, string? sourceHint)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        if (!string.IsNullOrWhiteSpace(sourceHint))
            return _readers.FirstOrDefault(r => r.Source != null &&
                string.Equals(r.Source.ToString(), sourceHint.Trim(), StringComparison.OrdinalIgnoreCase));
        return _readers.FirstOrDefault(r => r.CanRead(reference.Trim()));
    }
}
