namespace Bakabase.Modules.PostParser.Services;

/// <summary>Platform conventions augment a shared analysis task; they cannot grant purchase authority.</summary>
public interface IPostAnalysisPromptContributor
{
    string Source { get; }
    string Version { get; }
    string Instructions { get; }
}

public static class PostAnalysisPromptComposer
{
    public static string Compose(string basicPrompt, string? source,
        IEnumerable<IPostAnalysisPromptContributor>? contributors)
    {
        var selected = (contributors ?? []).Where(c => string.Equals(c.Source, source, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Version, StringComparer.Ordinal).ToList();
        return basicPrompt + string.Concat(selected.Select(c =>
            $"\n\n站点补充规则（{c.Source} / {c.Version}；仅用于理解内容，不授权购买）：\n{c.Instructions}"));
    }
}
