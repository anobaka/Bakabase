using System.Text.RegularExpressions;

namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// The one place the <c>{var:name}</c> syntax is parsed (capability map E4): the template
/// activity, future interpolating config fields, and the editor's lint all speak this format.
/// <para>Tokens: <c>{var:name}</c> — the variable's value; <c>{var:name:pad(3)}</c> — the value
/// left-padded with zeros; <c>{originalText}</c> — the working text entering the step.
/// Resolution order: the item's bag first, then its domain system variables — so a capture can
/// shadow a system fact.</para>
/// </summary>
public static partial class WorkflowVariableInterpolator
{
    [GeneratedRegex(@"\{var:(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::pad\((?<pad>\d{1,2})\))?\}|\{originalText\}")]
    private static partial Regex TokenRegex();

    /// <summary>Variable names referenced by a template (excluding <c>{originalText}</c>) —
    /// what the editor lints and <c>requiredVars</c> defaults could be derived from.</summary>
    public static IReadOnlyList<string> ReferencedVariables(string template) =>
        TokenRegex().Matches(template)
            .Where(m => m.Groups["name"].Success)
            .Select(m => m.Groups["name"].Value)
            .Distinct()
            .ToList();

    /// <summary>
    /// Render <paramref name="template"/>. A referenced variable found in neither the bag nor
    /// the system variables resolves to the empty string — <c>requiredVars</c> on the template
    /// activity is the mechanism for "missing must fail", not the interpolator.
    /// </summary>
    public static string Interpolate(
        string template,
        IReadOnlyDictionary<string, string> bag,
        IReadOnlyDictionary<string, string>? systemVariables,
        string originalText)
    {
        return TokenRegex().Replace(template, m =>
        {
            if (!m.Groups["name"].Success) return originalText;

            var name = m.Groups["name"].Value;
            var value = bag.TryGetValue(name, out var fromBag)
                ? fromBag
                : systemVariables?.GetValueOrDefault(name) ?? "";
            return m.Groups["pad"].Success
                ? value.PadLeft(int.Parse(m.Groups["pad"].Value), '0')
                : value;
        });
    }

    /// <summary>Whether <paramref name="name"/> resolves from the bag or system variables.</summary>
    public static bool CanResolve(
        string name,
        IReadOnlyDictionary<string, string> bag,
        IReadOnlyDictionary<string, string>? systemVariables) =>
        bag.ContainsKey(name) || (systemVariables?.ContainsKey(name) ?? false);
}
