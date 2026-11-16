using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Bakabase.Service.Components.Acquisition.Planning;

// A closed vocabulary of explicit publisher instructions. Preserve only IDs already written
// on the selected file or task. Never reconstruct a truncated ID, scan parent directories,
// vary case/prefix, or infer a password merely because a filename looks like a product.
internal static class PublisherPasswordReference
{
    private static readonly Regex Instruction = new(
        @"^(?:解[压壓](?:[缩縮])?(?:密[码碼]|[码碼])|密[码碼]|(?:archive |extraction |unzip )?password)\s*(?:[:：=]\s*)?(?:为|為|是|is\s+)?\s*(?:(?<prefix>RJ|BJ|VJ)\s*(?:[号號]|番[号號]|编号|編號|code)|(?:作品(?:编号|編號|番[号號]))|(?:the\s+)?(?:work|product|release)\s+(?:id|code))\s*[。.]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Identifier = new(@"(?<![a-zA-Z0-9])(?:RJ|BJ|VJ)[0-9]{5,12}(?![a-zA-Z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string? ReadInstruction(string line)
    {
        if (line.Length > 128) return null;
        var match = Instruction.Match(line);
        return !match.Success ? null : match.Groups["prefix"].Success
            ? match.Groups["prefix"].Value.ToUpperInvariant() : "*";
    }

    internal static IReadOnlyList<string> Resolve(string reference, string archive, string? title,
        IReadOnlySet<string> excluded)
    {
        var result = new List<string>(2);
        foreach (var label in new[] {Path.GetFileName(archive), title})
        {
            if (label is not {Length: > 0 and <= 4096}) continue;
            var ids = Identifier.Matches(label).Select(match => match.Value)
                .Where(value => reference == "*" || value.StartsWith(reference, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal).Take(2).ToArray();
            if (ids.Length != 1 || excluded.Contains(ids[0]) || result.Contains(ids[0], StringComparer.Ordinal)) continue;
            result.Add(ids[0]);
        }
        return result;
    }
}
