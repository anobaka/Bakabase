using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CsQuery;

namespace Bakabase.Service.Components.Acquisition.ProcessingPlans;

/// <summary>Projects only already-saved visible text. HTML entities are decoded once by the parser.</summary>
internal static class ProcessingPlanSourceText
{
    internal static string Visible(string html)
    {
        var text = new StringBuilder();
        var nodes = new Stack<(IDomObject Node, bool End)>(CQ.CreateFragment(html).Reverse().Select(n => (n, false)));
        while (nodes.TryPop(out var frame))
        {
            if (frame.End) {text.Append('\n'); continue;}
            var node = frame.Node;
            if (node.NodeType == NodeType.TEXT_NODE) {text.Append(node.NodeValue); continue;}
            if (node.NodeType != NodeType.ELEMENT_NODE) continue;
            var tag = node.NodeName.ToLowerInvariant();
            if (tag is "script" or "style" or "noscript" or "template" or "iframe" or "img" or "video" or "audio" ||
                node.HasAttribute("hidden") || node.GetAttribute("aria-hidden") == "true" ||
                Regex.IsMatch(node.GetAttribute("style") ?? "", @"(?:display\s*:\s*none|visibility\s*:\s*hidden)", RegexOptions.IgnoreCase)) continue;
            if (tag is "br" or "p" or "div" or "blockquote" or "li" or "tr" or "hr" or "h6")
            {
                text.Append('\n');
                if (tag != "br" && tag != "hr") nodes.Push((node, true));
            }
            // Source readers need the real link even when its visible label is simply
            // 'download'. The evidence envelope subsequently removes URLs/access codes.
            if (tag == "a" && Uri.TryCreate(node.GetAttribute("href"), UriKind.Absolute, out var href) && href.Scheme is "http" or "https")
                text.Append(' ').Append(href.AbsoluteUri).Append(' ');
            foreach (var child in node.ChildNodes.Reverse()) nodes.Push((child, false));
        }
        return text.ToString();
    }
}
