using Bakabase.Modules.HealthScore.Models;

namespace Bakabase.Modules.HealthScore.Components;

internal sealed class HealthScoreEngine : IHealthScoreEngine
{
    private readonly IResourceMatcherEvaluator _matcher;

    public HealthScoreEngine(IResourceMatcherEvaluator matcher)
    {
        _matcher = matcher;
    }

    public HealthScoreEvaluation Evaluate(HealthScoreProfile profile, ResourceMatcherEvaluationContext ctx)
    {
        var score = profile.BaseScore;
        var matched = new List<HealthScoreMatchedRule>();

        foreach (var rule in profile.Rules)
        {
            if (!_matcher.Evaluate(rule.Match, ctx))
            {
                continue;
            }

            score += rule.Delta;
            matched.Add(new HealthScoreMatchedRule
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                Delta = rule.Delta,
            });
        }

        return new HealthScoreEvaluation
        {
            Score = score,
            MatchedRules = matched,
        };
    }
}
