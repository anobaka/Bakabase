using System.Text.RegularExpressions;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// Factory + parser for Activity kind strings.
/// <para>Format: <c>{category}.{module}.{name}</c>, e.g. <c>action.downloader.exhentai.enqueue</c>.</para>
/// <para>All segments must be lowercase camelCase tokens; module may contain dots to nest
/// (so <c>downloader.exhentai</c> is one logical module).</para>
/// </summary>
public static class WorkflowActivityKinds
{
    // Each dot-separated segment starts with a lowercase letter, then any mix of letters
    // and digits — i.e. lowerCamelCase. `itemTitleContains` matches; `ItemTitleContains`
    // (PascalCase) and `Item_title` don't.
    private static readonly Regex SegmentPattern = new(@"^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)*$", RegexOptions.Compiled);

    public static string Filter(string module, string name)    => Build(WorkflowActivityCategory.Filter, module, name);
    public static string Action(string module, string name)    => Build(WorkflowActivityCategory.Action, module, name);
    public static string Transform(string module, string name) => Build(WorkflowActivityCategory.Transform, module, name);

    public static string Build(WorkflowActivityCategory category, string module, string name)
    {
        ValidateSegment(module, nameof(module));
        ValidateSegment(name, nameof(name));
        return $"{CategoryToken(category)}.{module}.{name}";
    }

    public static (WorkflowActivityCategory Category, string Module, string Name)? TryParse(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return null;
        var firstDot = kind.IndexOf('.');
        if (firstDot <= 0) return null;
        var categoryToken = kind[..firstDot];
        if (TryParseCategory(categoryToken) is not { } category) return null;

        var rest = kind[(firstDot + 1)..];
        var lastDot = rest.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == rest.Length - 1) return null;
        var module = rest[..lastDot];
        var name = rest[(lastDot + 1)..];
        return (category, module, name);
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

    private static string CategoryToken(WorkflowActivityCategory c) => c switch
    {
        WorkflowActivityCategory.Filter => "filter",
        WorkflowActivityCategory.Action => "action",
        WorkflowActivityCategory.Transform => "transform",
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, null),
    };

    private static WorkflowActivityCategory? TryParseCategory(string token) => token switch
    {
        "filter" => WorkflowActivityCategory.Filter,
        "action" => WorkflowActivityCategory.Action,
        "transform" => WorkflowActivityCategory.Transform,
        _ => null,
    };
}
