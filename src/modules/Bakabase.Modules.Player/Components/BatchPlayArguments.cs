using System.Text;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.Player.Components;

/// <summary>
/// Builds player command lines. Mirrors the "{0}" template semantics used by
/// profile players in LocalFilePlayableItemProvider so a player configured
/// for single-file play behaves identically when handed a playlist file.
/// </summary>
public static partial class BatchPlayArguments
{
    [GeneratedRegex("""(["']?)\{(\d+)\}(["']?)""")]
    private static partial Regex PlaceholderRegex();

    /// <summary>
    /// Substitutes the single target (a media file or a playlist file) into
    /// the command template, quoting it unless the template already quotes
    /// the placeholder.
    /// </summary>
    public static string BuildFromTemplate(string? commandTemplate, string targetPath)
    {
        var template = string.IsNullOrEmpty(commandTemplate) ? "{0}" : commandTemplate;
        var escaped = targetPath.Replace("\"", "\\\"");
        return PlaceholderRegex().Replace(template, match =>
        {
            var prefix = match.Groups[1].Value;
            var suffix = match.Groups[3].Value;
            var alreadyQuoted = (prefix == "\"" && suffix == "\"") ||
                                (prefix == "'" && suffix == "'");
            return alreadyQuoted
                ? $"{prefix}{escaped}{suffix}"
                : $"\"{escaped}\"";
        });
    }

    /// <summary>
    /// Joins multiple file paths into one command line, each quoted.
    /// </summary>
    public static string BuildMultiFile(IEnumerable<string> files)
    {
        var sb = new StringBuilder();
        foreach (var file in files)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append('"').Append(file.Replace("\"", "\\\"")).Append('"');
        }

        return sb.ToString();
    }
}
