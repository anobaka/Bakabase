using System.Net;
using CsQuery;

namespace Bakabase.Modules.ThirdParty.Helpers;

/// <summary>Captures inner HTML that can be parsed again without changing text or attribute values.</summary>
public static class HtmlContentSnapshot
{
    public static string Capture(CQ content)
    {
        // CsQuery encodes text nodes, but its default renderer writes ampersands in attribute
        // values literally. Encode attributes on a detached copy so a later HTML parse does not
        // decode a visible literal such as "&amp;" for a second time. The reader's DOM stays intact.
        var copy = content.Clone();
        foreach (var node in copy.Find("*").Concat(copy).Distinct())
        {
            if (!node.HasAttributes) continue;
            foreach (var attribute in node.Attributes.ToArray())
                node.Cq().Attr(attribute.Key, WebUtility.HtmlEncode(attribute.Value));
        }
        return copy.Html();
    }
}
