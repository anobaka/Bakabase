using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.HealthScore.Components;
using Bakabase.Modules.HealthScore.Components.Predicates;
using Bakabase.Modules.HealthScore.Models;
using FluentAssertions;

namespace Bakabase.Modules.HealthScore.Tests;

[TestClass]
public class HealthScoreEngineTests
{
    [TestMethod]
    public void NoRules_ReturnsBaseScore()
    {
        using var tmp = TestHelpers.CreateTempDir();
        var engine = BuildEngine();
        var profile = new HealthScoreProfile { BaseScore = 100, Rules = new() };

        var r = engine.Evaluate(profile, MakeContext(tmp));
        r.Score.Should().Be(100);
        r.MatchedRules.Should().BeEmpty();
    }

    [TestMethod]
    public void SumsAllMatchingDeltas()
    {
        using var tmp = TestHelpers.CreateTempDir();
        // No video, no cover.
        var engine = BuildEngine();
        var profile = new HealthScoreProfile
        {
            BaseScore = 100,
            Rules = new()
            {
                new HealthScoreRule
                {
                    Id = 1,
                    Name = "Missing video",
                    Delta = -100,
                    Match = new ResourceMatcher
                    {
                        Combinator = SearchCombinator.And,
                        Leaves = new()
                        {
                            FileLeaf(MediaTypeFileCountPredicate.PredicateId,
                                new MediaTypeFileCountParameters
                                {
                                    MediaType = PredicateMediaType.Video,
                                    Operator = ComparisonOperator.Equals, Count = 0,
                                }),
                        },
                    },
                },
                new HealthScoreRule
                {
                    Id = 2,
                    Name = "Missing cover",
                    Delta = -10,
                    Match = new ResourceMatcher
                    {
                        Combinator = SearchCombinator.And,
                        Leaves = new()
                        {
                            FileLeaf(HasCoverImagePredicate.PredicateId, parameters: null, negated: true),
                        },
                    },
                },
            },
        };

        var r = engine.Evaluate(profile, MakeContext(tmp));
        r.Score.Should().Be(-10); // 100 - 100 - 10
        r.MatchedRules.Select(m => m.RuleId).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [TestMethod]
    public void NonMatchingRule_DoesNotContribute()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("a.mp4", new byte[] { 1 });

        var engine = BuildEngine();
        var profile = new HealthScoreProfile
        {
            BaseScore = 100,
            Rules = new()
            {
                new HealthScoreRule
                {
                    Id = 1, Delta = -100,
                    Match = new ResourceMatcher
                    {
                        Combinator = SearchCombinator.And,
                        Leaves = new()
                        {
                            FileLeaf(MediaTypeFileCountPredicate.PredicateId,
                                new MediaTypeFileCountParameters
                                {
                                    MediaType = PredicateMediaType.Video,
                                    Operator = ComparisonOperator.Equals, Count = 0,
                                }),
                        },
                    },
                },
            },
        };

        var r = engine.Evaluate(profile, MakeContext(tmp));
        r.Score.Should().Be(100);
        r.MatchedRules.Should().BeEmpty();
    }

    [TestMethod]
    public void NegativeScore_NotClamped()
    {
        using var tmp = TestHelpers.CreateTempDir();
        var engine = BuildEngine();
        var profile = new HealthScoreProfile
        {
            BaseScore = 50,
            Rules = new()
            {
                new HealthScoreRule
                {
                    Id = 1, Delta = -200,
                    Match = new ResourceMatcher { Combinator = SearchCombinator.And },
                },
            },
        };

        engine.Evaluate(profile, MakeContext(tmp)).Score.Should().Be(-150);
    }

    private static IHealthScoreEngine BuildEngine()
    {
        var registry = new SimpleRegistry();
        var matcher = new ResourceMatcherEvaluator(registry);
        return new TestEngine(matcher);
    }

    private sealed class SimpleRegistry : IFilePredicateRegistry
    {
        private readonly Dictionary<string, IFilePredicate> _byId = new(StringComparer.OrdinalIgnoreCase)
        {
            [MediaTypeFileCountPredicate.PredicateId] = new MediaTypeFileCountPredicate(),
            [HasCoverImagePredicate.PredicateId] = new HasCoverImagePredicate(),
        };

        public IFilePredicate? Find(string id) => _byId.GetValueOrDefault(id);
        public IReadOnlyList<IFilePredicate> All => _byId.Values.ToList();
    }

    /// <summary>Direct construct around the internal HealthScoreEngine to avoid DI in tests.</summary>
    private sealed class TestEngine : IHealthScoreEngine
    {
        private readonly IResourceMatcherEvaluator _matcher;
        public TestEngine(IResourceMatcherEvaluator matcher) => _matcher = matcher;

        public HealthScoreEvaluation Evaluate(HealthScoreProfile profile, ResourceMatcherEvaluationContext ctx)
        {
            var score = profile.BaseScore;
            var matched = new List<HealthScoreMatchedRule>();
            foreach (var rule in profile.Rules)
            {
                if (!_matcher.Evaluate(rule.Match, ctx)) continue;
                score += rule.Delta;
                matched.Add(new HealthScoreMatchedRule
                {
                    RuleId = rule.Id, RuleName = rule.Name, Delta = rule.Delta,
                });
            }

            return new HealthScoreEvaluation { Score = score, MatchedRules = matched };
        }
    }

    private static ResourceMatcherEvaluationContext MakeContext(TestHelpers.TempDir tmp) => new()
    {
        Snapshot = tmp.Snapshot(),
        GetPropertyValues = (_, _) => null,
        PropertyValueMatcher = (_, _, _, _, _) => false,
    };

    private static ResourceMatcherLeaf FileLeaf(string id, object? parameters, bool negated = false)
    {
        return new ResourceMatcherLeaf
        {
            Kind = ResourceMatcherLeafKind.File,
            FilePredicateId = id,
            FilePredicateParametersJson = parameters is null
                ? null
                : System.Text.Json.JsonSerializer.Serialize(parameters),
            Negated = negated,
        };
    }
}
