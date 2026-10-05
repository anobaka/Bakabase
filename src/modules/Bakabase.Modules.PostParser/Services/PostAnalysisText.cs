using System.Text.Json;
using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

internal static class PostAnalysisText
{
    public static string Render(PostContent content) => JsonSerializer.Serialize(new
    {
        title = content.Title,
        mainHtml = content.MainHtml,
        comments = content.Comments.Count > 0 ? (object)content.Comments : content.CommentHtmlList,
        scope = content.Scope,
        sourceUrl = content.SourceUrl,
        note = "Only the captured content is available. Hidden content and later pages are unknown."
    });
}
