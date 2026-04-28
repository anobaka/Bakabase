using Bakabase.Modules.HealthScore.Components;

namespace Bakabase.Modules.HealthScore.Components.Predicates;

/// <summary>
/// Matches when the resource has a cover image. Use with Negated=true to score
/// "missing cover".
/// </summary>
public sealed class HasCoverImagePredicate : FilePredicate
{
    public const string PredicateId = "HasCoverImage";
    public override string Id => PredicateId;

    protected override bool EvaluateInternal(ResourceFsSnapshot snapshot) => snapshot.HasCoverImage;
}
