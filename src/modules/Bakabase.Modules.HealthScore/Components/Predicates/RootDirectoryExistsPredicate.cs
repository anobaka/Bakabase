using Bakabase.Modules.HealthScore.Components;

namespace Bakabase.Modules.HealthScore.Components.Predicates;

/// <summary>
/// Matches when the resource's root path still exists on disk. Use with
/// Negated=true to score "resource lost".
/// </summary>
public sealed class RootDirectoryExistsPredicate : FilePredicate
{
    public const string PredicateId = "RootDirectoryExists";
    public override string Id => PredicateId;

    protected override bool EvaluateInternal(ResourceFsSnapshot snapshot) => snapshot.RootExists;
}
