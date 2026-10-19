using System.Text.Encodings.Web;
using System.Text.Json;
using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

internal static class PostAnalysisText
{
    // This JSON is model input, never markup embedded in a page. Keep Unicode and URLs readable
    // instead of making the model tokenize literal \uXXXX escapes inside the user message.
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Render(PostContent content)
    {
        var pastedText = string.Equals(content.Scope, "pastedText", StringComparison.OrdinalIgnoreCase);
        // Historical readers decoded entities before saving HTML. A restored or externally supplied
        // snapshot has no encoding guarantee, so parsing it again could change an entity-like password.
        var compactHtml = content.HtmlEncodingTrusted && !pastedText;
        string Compact(string text) => compactHtml ? PostAnalysisHtml.Render(text) : text;
        return JsonSerializer.Serialize(new
        {
            title = content.Title,
            mainHtml = Compact(content.MainHtml),
            comments = content.Comments.Count > 0
                ? (object)content.Comments.Select(c => c with {Html = Compact(c.Html)})
                : content.CommentHtmlList.Select(Compact),
            scope = content.Scope,
            sourceUrl = content.SourceUrl,
            note = "Only the captured content is available. Hidden content and later pages are unknown."
        }, Json);
    }
}
