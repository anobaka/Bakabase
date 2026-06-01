using System.Text.RegularExpressions;

namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// Factory + parser for Trigger kind strings.
/// <para>Format: <c>{source}.{event}</c>, e.g. <c>subscription.updated</c>.</para>
/// </summary>
public static class WorkflowTriggerKinds
{
    // Mirrors WorkflowActivityKinds — lowerCamelCase segments separated by dots.
    private static readonly Regex SegmentPattern = new(@"^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)*$", RegexOptions.Compiled);

    public static string Build(string source, string @event)
    {
        ValidateSegment(source, nameof(source));
        ValidateSegment(@event, nameof(@event));
        return $"{source}.{@event}";
    }

    public static (string Source, string Event)? TryParse(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return null;
        var lastDot = kind.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == kind.Length - 1) return null;
        return (kind[..lastDot], kind[(lastDot + 1)..]);
    }

    private static void ValidateSegment(string s, string paramName)
    {
        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("required", paramName);
        if (!SegmentPattern.IsMatch(s))
            throw new ArgumentException(
                $"\"{s}\" must be one or more lowerCamelCase tokens separated by dots " +
                "(each segment starts with a lowercase letter, then letters or digits)", paramName);
    }
}
