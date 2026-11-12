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
            {
                var encoded = WebUtility.HtmlEncode(attribute.Value);
                if (encoded == attribute.Value) continue;
                // This is serialization of a copy, not a form edit. CQ.Attr applies jQuery's
                // immutable input/button type hook to full-document clones, even for unchanged
                // values (CsQuery/CQ_jQuery/Attr.cs). Set only values that need escaping, directly
                // on the DOM, so type and boolean form-field semantics stay intact.
                node.SetAttribute(attribute.Key, encoded);
            }
        }
        return copy.Html();
    }
}
