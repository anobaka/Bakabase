using System.Text;
using CsQuery;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>Removes presentation markup without selecting, truncating or rewriting post content.</summary>
internal static class PostAnalysisHtml
{
    private static readonly HashSet<string> HtmlElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "abbr", "acronym", "address", "area", "article", "aside", "audio", "b", "base", "bdi", "bdo",
        "big", "blockquote", "body", "br", "button", "caption", "center", "cite", "code", "col", "colgroup",
        "dd", "del", "details", "dfn", "dialog", "div", "dl", "dt", "em", "embed", "fieldset", "figcaption",
        "figure", "font", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "head", "header", "hgroup",
        "hr", "html", "i", "iframe", "img", "input", "ins", "kbd", "label", "legend", "li", "link", "main",
        "map", "mark", "menu", "meta", "nav", "nobr", "noscript", "object", "ol", "optgroup", "option",
        "p", "param", "picture", "pre", "q", "rp", "rt", "ruby", "s", "samp", "script", "section", "select",
        "small", "source", "span", "strike", "strong", "style", "sub", "summary", "sup", "table", "tbody",
        "td", "template", "textarea", "tfoot", "th", "thead", "time", "title", "tr", "track", "tt", "u",
        "ul", "var", "video", "wbr"
    };

    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "br", "caption", "center", "dd", "details", "dialog",
        "div", "dl", "dt", "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4",
        "h5", "h6", "header", "hgroup", "hr", "legend", "li", "main", "menu", "nav", "ol", "p", "pre",
        "section", "summary", "table", "tbody", "tfoot", "thead", "tr", "ul"
    };

    // Quote/deletion boundaries and literal code matter to expiry evidence, passwords and file steps.
    // Lists and tables retain their structure so stripping layout cannot merge independent instructions.
    private static readonly HashSet<string> SemanticElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "blockquote", "q", "del", "s", "strike", "ins", "pre", "code", "ol", "ul", "li", "table", "tr", "td", "th", "sub", "sup",
        "script", "style"
    };

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"
    };

    // Inline style may hold relative/data URLs or literal credential text, so retain it in full.
    private static readonly HashSet<string> LayoutAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "class", "width", "height", "align", "valign", "border", "cellpadding", "cellspacing",
        "color", "bgcolor", "face", "size", "target", "rel", "loading", "decoding", "referrerpolicy", "crossorigin", "tabindex"
    };

    public static string Render(string html)
    {
        // Plain text (including literal angle brackets and entity-looking passwords) is left alone.
        // Unrecognised tags or incomplete attributes are ambiguous: retain the source in that case.
        if (string.IsNullOrEmpty(html) || !HasSupportedMarkup(html)) return html;

        try
        {
            var roots = CQ.CreateFragment(html);
            var output = new StringBuilder(html.Length);
            var pending = new Stack<(IDomObject Node, bool Closing)>();
            foreach (var node in roots.Reverse()) pending.Push((node, false));

            while (pending.TryPop(out var frame))
            {
                var node = frame.Node;
                if (node.NodeType == NodeType.TEXT_NODE)
                {
                    // CsQuery decodes normal HTML text once and leaves script/style raw text alone.
                    // Decoding again would change entity-looking passwords and signed URL parameters.
                    output.Append(node.NodeValue);
                    continue;
                }
                if (node.NodeType == NodeType.COMMENT_NODE)
                {
                    // An author may have put a URL or password in a comment. Keep its content and
                    // its context instead of assuming that every invisible fragment is disposable.
                    output.Append("<!--").Append(node.NodeValue).Append("-->");
                    continue;
                }
                if (node.NodeType != NodeType.ELEMENT_NODE) continue;
                var name = node.NodeName.ToLowerInvariant();
                var presentationMeaning = SemanticPresentation(node);
                var keepBoundary = SemanticElements.Contains(name) || presentationMeaning.Length > 0;

                if (frame.Closing)
                {
                    if (keepBoundary) output.Append("</").Append(name).Append('>');
                    AppendContentAttributes(output, node);
                    if (BlockElements.Contains(name)) AppendLineBreak(output);
                    continue;
                }

                if (BlockElements.Contains(name)) AppendLineBreak(output);
                if (keepBoundary) output.Append('<').Append(name).Append('>').Append(presentationMeaning);
                pending.Push((node, true));
                if (node.ChildNodes is { } children)
                    foreach (var child in children.Reverse()) pending.Push((child, false));
            }

            return output.ToString();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            // Compaction must never turn an unusual, otherwise parseable post into missing data.
            return html;
        }
    }

    private static void AppendContentAttributes(StringBuilder output, IDomObject node)
    {
        if (!node.HasAttributes) return;
        foreach (var attribute in node.Attributes)
        {
            if (!LayoutAttributes.Contains(attribute.Key) || ContainsUrl(attribute.Value))
            {
                // Keep the target next to its label/image, with its original query, fragment and
                // percent escapes. No URI normalisation, URL decoding or credential whitespace trim.
                output.Append(" [").Append(attribute.Key).Append('=').Append(attribute.Value).Append(']');
            }
        }
    }

    private static bool ContainsUrl(string value) =>
        value.Contains("://", StringComparison.Ordinal) ||
        value.Contains("magnet:", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("ed2k:", StringComparison.OrdinalIgnoreCase);

    private static string SemanticPresentation(IDomObject node)
    {
        var classes = node.GetAttribute("class") ?? "";
        var style = node.GetAttribute("style") ?? "";
        var compactStyle = string.Concat(style.Where(c => !char.IsWhiteSpace(c)));
        var result = new StringBuilder();
        if (new[] {"quote", "strike", "deleted", "spoiler", "hidden"}.Any(value => classes.Contains(value, StringComparison.OrdinalIgnoreCase)))
            result.Append(" [class=").Append(classes).Append(']');
        if (new[] {"line-through", "display:none", "visibility:hidden", "white-space:pre"}.Any(value => compactStyle.Contains(value, StringComparison.OrdinalIgnoreCase)))
            result.Append(" [style=").Append(style).Append(']');
        return result.ToString();
    }

    private static void AppendLineBreak(StringBuilder output)
    {
        if (output.Length > 0 && output[^1] != '\n') output.Append('\n');
    }

    private static bool HasSupportedMarkup(string html)
    {
        var hasMarkup = false;
        var openElements = new Stack<string>();
        var index = 0;
        while ((index = html.IndexOf('<', index)) >= 0)
        {
            if (html.AsSpan(index).StartsWith("<!--", StringComparison.Ordinal))
            {
                var commentEnd = html.IndexOf("-->", index + 4, StringComparison.Ordinal);
                if (commentEnd < 0) return false;
                hasMarkup = true;
                index = commentEnd + 3;
                continue;
            }

            var nameStart = index + 1;
            var closing = nameStart < html.Length && html[nameStart] == '/';
            if (closing) nameStart++;
            if (nameStart >= html.Length || !char.IsAsciiLetter(html[nameStart]))
            {
                index++;
                continue;
            }

            var nameEnd = nameStart;
            while (nameEnd < html.Length && (char.IsAsciiLetterOrDigit(html[nameEnd]) || html[nameEnd] is '-' or ':')) nameEnd++;
            var name = html[nameStart..nameEnd];
            if (!HtmlElements.Contains(name) || nameEnd == html.Length ||
                !(char.IsWhiteSpace(html[nameEnd]) || html[nameEnd] is '/' or '>')) return false;
            var tagEnd = FindTagEnd(html, nameEnd);
            if (tagEnd < 0) return false;
            hasMarkup = true;
            index = tagEnd + 1;

            if (!closing && (name.Equals("script", StringComparison.OrdinalIgnoreCase) || name.Equals("style", StringComparison.OrdinalIgnoreCase)))
            {
                // Their contents are raw text, so JavaScript comparisons and quoted markup must not
                // be mistaken for broken HTML. An unterminated raw-text element keeps the source.
                var rawEnd = html.IndexOf("</" + name, index, StringComparison.OrdinalIgnoreCase);
                if (rawEnd < 0) return false;
                var rawTagEnd = FindTagEnd(html, rawEnd + name.Length + 2);
                if (rawTagEnd < 0) return false;
                index = rawTagEnd + 1;
            }
            else if (closing)
            {
                if (openElements.Count == 0 || !openElements.Pop().Equals(name, StringComparison.OrdinalIgnoreCase)) return false;
            }
            else if (!VoidElements.Contains(name) && html[tagEnd - 1] != '/') openElements.Push(name);
        }
        return hasMarkup && openElements.Count == 0;
    }

    private static int FindTagEnd(string html, int index)
    {
        var quote = '\0';
        for (; index < html.Length; index++)
        {
            var value = html[index];
            if (quote != '\0')
            {
                if (value == quote) quote = '\0';
            }
            else if (value is '\'' or '"') quote = value;
            else if (value == '>') return index;
            else if (value == '<') return -1;
        }
        return -1;
    }
}
