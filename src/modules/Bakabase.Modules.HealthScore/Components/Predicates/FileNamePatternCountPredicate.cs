using System.Text.RegularExpressions;
using Bakabase.Modules.HealthScore.Components;

namespace Bakabase.Modules.HealthScore.Components.Predicates;

public record FileNamePatternCountParameters
{
    /// <summary>Regex against the file name (not full path).</summary>
    public string Pattern { get; set; } = string.Empty;
    public ComparisonOperator Operator { get; set; }
    public int Count { get; set; }
}

public sealed class FileNamePatternCountPredicate : FilePredicate<FileNamePatternCountParameters>
{
    public const string PredicateId = "FileNamePatternCount";
    public override string Id => PredicateId;

    protected override bool EvaluateInternal(ResourceFsSnapshot snapshot, FileNamePatternCountParameters? p)
    {
        if (p is null || string.IsNullOrEmpty(p.Pattern)) return false;
        Regex regex;
        try
        {
            regex = new Regex(p.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return false;
        }

        var count = snapshot.AllFiles.Count(f => regex.IsMatch(f.Name));
        return p.Operator.Apply(count, p.Count);
    }
}
