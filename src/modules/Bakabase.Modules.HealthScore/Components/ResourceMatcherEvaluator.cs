using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.HealthScore.Models;

namespace Bakabase.Modules.HealthScore.Components;

public sealed class ResourceMatcherEvaluator : IResourceMatcherEvaluator
{
    private readonly IFilePredicateRegistry _predicates;

    public ResourceMatcherEvaluator(IFilePredicateRegistry predicates)
    {
        _predicates = predicates;
    }

    public bool Evaluate(ResourceMatcher matcher, ResourceMatcherEvaluationContext ctx) =>
        EvaluateGroup(matcher, ctx);

    private bool EvaluateGroup(ResourceMatcher group, ResourceMatcherEvaluationContext ctx)
    {
        if (group.Disabled)
        {
            // A disabled group is neutral: in And it must not flip, in Or it must not contribute.
            // We encode that by treating it as "true" in And and "false" in Or, matching the
            // identity of each combinator.
            return group.Combinator == SearchCombinator.And;
        }

        var leafResults = group.Leaves?
            .Where(l => !l.Disabled)
            .Select(l => EvaluateLeaf(l, ctx)) ?? [];

        var groupResults = group.Groups?
            .Where(g => !g.Disabled)
            .Select(g => EvaluateGroup(g, ctx)) ?? [];

        var all = leafResults.Concat(groupResults).ToList();

        if (all.Count == 0)
        {
            // Empty group is vacuously true (matches "no constraints").
            return true;
        }

        return group.Combinator switch
        {
            SearchCombinator.And => all.All(b => b),
            SearchCombinator.Or => all.Any(b => b),
            _ => false,
        };
    }

    private bool EvaluateLeaf(ResourceMatcherLeaf leaf, ResourceMatcherEvaluationContext ctx)
    {
        var raw = leaf.Kind switch
        {
            ResourceMatcherLeafKind.Property => EvaluatePropertyLeaf(leaf, ctx),
            ResourceMatcherLeafKind.File => EvaluateFileLeaf(leaf, ctx),
            _ => false,
        };

        return leaf.Negated ? !raw : raw;
    }

    private static bool EvaluatePropertyLeaf(ResourceMatcherLeaf leaf, ResourceMatcherEvaluationContext ctx)
    {
        if (leaf.PropertyPool is not { } pool ||
            leaf.PropertyId is not { } pid ||
            leaf.Operation is not { } op ||
            ctx.PropertyValueMatcher is null)
        {
            return false;
        }

        var values = ctx.GetPropertyValues(pool, pid);
        if (values is null || values.Count == 0)
        {
            return ctx.PropertyValueMatcher(pool, pid, op, null, leaf.PropertyDbValue);
        }

        foreach (var v in values)
        {
            if (ctx.PropertyValueMatcher(pool, pid, op, v, leaf.PropertyDbValue))
            {
                return true;
            }
        }

        return false;
    }

    private bool EvaluateFileLeaf(ResourceMatcherLeaf leaf, ResourceMatcherEvaluationContext ctx)
    {
        if (string.IsNullOrEmpty(leaf.FilePredicateId)) return false;
        var predicate = _predicates.Find(leaf.FilePredicateId);
        return predicate is not null && predicate.Evaluate(ctx.Snapshot, leaf.FilePredicateParametersJson);
    }
}
