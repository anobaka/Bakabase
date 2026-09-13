using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>
/// Works out what the folder an acquired resource lands in should be called.
/// <para>
/// A relative-path template over the work item, sanitized and budgeted one component at a time.
/// Only separators written in the template create subdirectories; values remain single names.
/// It is deliberately not the
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
        var parts = TemplateParts(template);
        var rendered = new List<string>();

        for (var i = 0; i < parts.Length; i++)
        {
            var part = Placeholder.Replace(parts[i], m =>
            {
                var value = FileNameSanitizer.Sanitize(values.GetValueOrDefault(m.Groups["key"].Value) ?? "");

                return string.IsNullOrWhiteSpace(value) ? Nothing : value;
            });

            part = DropEmptyWrappers(part);
            part = Regex.Replace(part.Replace(Nothing, ""), @"\s{2,}", " ").Trim();
            var safe = Budget(FileNameSanitizer.Sanitize(part));

            if (string.IsNullOrWhiteSpace(safe))
            {
                // Missing categories can disappear; the resource itself must still have a leaf.
                if (i < parts.Length - 1) continue;
                safe = Budget(FileNameSanitizer.Sanitize(item.Title ?? ""));
                if (string.IsNullOrWhiteSpace(safe)) safe = $"acquisition-{item.ResourceId}";
            }

            rendered.Add(safe);
        }

        return Path.Combine(rendered.ToArray());
    }

    /// <summary>Rejects paths that cannot name a descendant of the configured library.</summary>
    public static void ValidateTemplate(string? template) => TemplateParts(template);

    public static bool HasSubdirectories(string? template) => TemplateParts(template).Length > 1;

    /// <summary>Resolves a rendered relative name, including libraries rooted at a filesystem root.</summary>
    public static string ResolveTargetDirectory(string libraryRoot, string relativeName)
    {
        var root = Path.GetFullPath(libraryRoot);
        var target = Path.GetFullPath(Path.Combine(root, relativeName));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || target == root)
        {
            throw new ArgumentException("The resource directory must stay inside the library.", nameof(relativeName));
        }

        return target;
    }

    private static string[] TemplateParts(string? template)
    {
        var text = string.IsNullOrWhiteSpace(template) ? "{Title}" : template.Trim();

        // Check both slash styles and drive prefixes on every OS, including drive-relative C:foo.
        if (text.StartsWith('/') || text.StartsWith('\\') || Regex.IsMatch(text, @"^[A-Za-z]:"))
        {
            throw new ArgumentException("The directory template must be a relative path inside the library.",
                nameof(template));
        }

        var parts = text.Split(['/', '\\']);
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part.Trim() is "." or ".."))
        {
            throw new ArgumentException("The directory template cannot contain empty, '.' or '..' path segments.",
                nameof(template));
        }

        return parts;
    }

    private static string Budget(string name) =>
        name.Length <= MaxLength ? name : name[..MaxLength].TrimEnd('.', ' ');

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
