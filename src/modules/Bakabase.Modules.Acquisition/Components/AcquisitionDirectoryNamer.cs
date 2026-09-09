using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>
/// Works out what the folder an acquired resource lands in should be called.
/// <para>
/// A template over the work item, then sanitized and budgeted. It is deliberately not the
/// display-name template machinery: that exists to render a name for a resource that already has
/// properties, and this runs before the resource has any. What it does borrow is the one behaviour
/// that matters — a placeholder that resolves to nothing takes its brackets with it, so a template
/// like <c>{Title} [{Circle}]</c> does not leave an empty pair of brackets behind.
/// </para>
/// </summary>
public static class AcquisitionDirectoryNamer
{
    /// <summary>
    /// Most filesystems stop at 255 bytes per component, and the path this name sits in is already
    /// some of the budget. Well under the limit rather than exactly at it.
    /// </summary>
    public const int MaxLength = 180;

    private static readonly Regex Placeholder = new(@"\{(?<key>[A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Stands in for a placeholder that resolved to nothing, just long enough to tell "the user
    /// typed brackets around something empty" from "the user typed empty brackets".
    /// </summary>
    private const string Nothing = "\uE000";

    /// <summary>Bracket pairs that wrap an optional part of a name.</summary>
    private static readonly (char Left, char Right)[] Wrappers =
    [
        ('[', ']'), ('(', ')'), ('{', '}'), ('（', '）'), ('【', '】'), ('「', '」')
    ];

    public static string Render(string? template, AcquisitionWorkItem item)
    {
        var values = ValuesOf(item);
        var text = string.IsNullOrWhiteSpace(template) ? "{Title}" : template;

        var rendered = Placeholder.Replace(text, m =>
            values.GetValueOrDefault(m.Groups["key"].Value) is {Length: > 0} v ? v : Nothing);

        rendered = DropEmptyWrappers(rendered);
        rendered = Regex.Replace(rendered.Replace(Nothing, ""), @"\s{2,}", " ").Trim();

        var safe = FileNameSanitizer.Sanitize(rendered);

        if (string.IsNullOrWhiteSpace(safe))
        {
            // Everything the template referred to was empty, or the whole name sanitized away.
            safe = FileNameSanitizer.Sanitize(item.Title ?? "");
        }

        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = $"acquisition-{item.ResourceId}";
        }

        return safe.Length <= MaxLength ? safe : safe[..MaxLength].TrimEnd('.', ' ');
    }

    /// <summary>
    /// What a template may refer to. The item's own variables come last so a step that captured
    /// something can override a built-in of the same name.
    /// </summary>
    private static Dictionary<string, string> ValuesOf(AcquisitionWorkItem item)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = item.Title ?? "",
            ["ResourceId"] = item.ResourceId.ToString(),
            ["LeadKind"] = item.LeadKind.ToString(),
            ["Date"] = DateTime.Now.ToString("yyyy-MM-dd"),
        };

        foreach (var (key, value) in item.Variables)
        {
            values[key] = value;
        }

        return values;
    }

    /// <summary>
    /// Removes a bracket pair whose entire contents came from placeholders that resolved to
    /// nothing. Anything the user typed literally inside the brackets keeps them.
    /// </summary>
    private static string DropEmptyWrappers(string text)
    {
        foreach (var (left, right) in Wrappers)
        {
            var pattern = Regex.Escape(left.ToString()) + "[\\s" + Nothing + "]*" +
                          Regex.Escape(right.ToString());

            text = Regex.Replace(text, pattern, "");
        }

        return text;
    }
}
