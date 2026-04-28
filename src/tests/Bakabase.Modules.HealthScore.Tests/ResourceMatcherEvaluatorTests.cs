using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.HealthScore.Components.Predicates;
using Bakabase.Modules.HealthScore.Components;
using Bakabase.Modules.HealthScore.Models;
using FluentAssertions;

namespace Bakabase.Modules.HealthScore.Tests;

[TestClass]
public class ResourceMatcherEvaluatorTests
{
    private static IResourceMatcherEvaluator BuildEvaluator() =>
        new ResourceMatcherEvaluator(new TestPredicateRegistry());

    /// <summary>Local registry that includes the predicate set we test against.</summary>
    private sealed class TestPredicateRegistry : IFilePredicateRegistry
    {
        private readonly Dictionary<string, IFilePredicate> _byId;

        public TestPredicateRegistry()
        {
            var predicates = new IFilePredicate[]
            {
                new MediaTypeFileCountPredicate(),
                new MediaTypeTotalSizePredicate(),
                new HasCoverImagePredicate(),
                new RootDirectoryExistsPredicate(),
                new FileNamePatternCountPredicate(),
                new FileSizeOutOfRangePredicate(),
            };
            _byId = predicates.ToDictionary(p => p.Id, p => p, StringComparer.OrdinalIgnoreCase);
        }

        public IFilePredicate? Find(string id) => _byId.GetValueOrDefault(id);
        public IReadOnlyList<IFilePredicate> All => _byId.Values.ToList();
    }

    [TestMethod]
    public void Empty_Group_IsTrue()
    {
        using var tmp = TestHelpers.CreateTempDir();
        var evaluator = BuildEvaluator();
        var matcher = new ResourceMatcher { Combinator = SearchCombinator.And };

        var ctx = MakeContext(tmp);
        evaluator.Evaluate(matcher, ctx).Should().BeTrue();
    }

    [TestMethod]
    public void And_RequiresAllLeaves()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("a.mp4", new byte[] { 1 });
        var evaluator = BuildEvaluator();
        var matcher = new ResourceMatcher
        {
            Combinator = SearchCombinator.And,
            Leaves = new()
            {
                FileLeaf(MediaTypeFileCountPredicate.PredicateId, new MediaTypeFileCountParameters
                {
                    MediaType = PredicateMediaType.Video,
                    Operator = ComparisonOperator.GreaterThan, Count = 0,
                }),
                FileLeaf(MediaTypeFileCountPredicate.PredicateId, new MediaTypeFileCountParameters
                {
                    MediaType = PredicateMediaType.Image,
                    Operator = ComparisonOperator.GreaterThan, Count = 0,
                }),
            },
        };

        evaluator.Evaluate(matcher, MakeContext(tmp)).Should().BeFalse(); // no images

        tmp.Touch("b.jpg", new byte[] { 1 });
        evaluator.Evaluate(matcher, MakeContext(tmp)).Should().BeTrue();
    }

    [TestMethod]
    public void Or_TakesAnyLeaf()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("a.mp4", new byte[] { 1 });
        var evaluator = BuildEvaluator();
        var matcher = new ResourceMatcher
        {
            Combinator = SearchCombinator.Or,
            Leaves = new()
            {
                FileLeaf(MediaTypeFileCountPredicate.PredicateId, new MediaTypeFileCountParameters
                {
                    MediaType = PredicateMediaType.Video,
                    Operator = ComparisonOperator.GreaterThan, Count = 0,
                }),
                FileLeaf(MediaTypeFileCountPredicate.PredicateId, new MediaTypeFileCountParameters
                {
                    MediaType = PredicateMediaType.Image,
                    Operator = ComparisonOperator.GreaterThan, Count = 0,
                }),
            },
        };

        evaluator.Evaluate(matcher, MakeContext(tmp)).Should().BeTrue();
    }

    [TestMethod]
    public void Negation_FlipsLeaf()
    {
        using var tmp = TestHelpers.CreateTempDir();
        // No files: HasCoverImage is false. Negate -> true.
        var evaluator = BuildEvaluator();
        var matcher = new ResourceMatcher
        {
            Combinator = SearchCombinator.And,
            Leaves = new()
            {
                FileLeaf(HasCoverImagePredicate.PredicateId, parameters: null, negated: true),
            },
        };

        evaluator.Evaluate(matcher, MakeContext(tmp)).Should().BeTrue();
    }

    [TestMethod]
    public void DisabledLeaf_Ignored()
    {
        using var tmp = TestHelpers.CreateTempDir();
        var evaluator = BuildEvaluator();
        var leaf = FileLeaf(HasCoverImagePredicate.PredicateId, null);
        leaf.Disabled = true;

        var matcher = new ResourceMatcher
        {
            Combinator = SearchCombinator.And,
            Leaves = new() { leaf },
        };
        // With the only leaf disabled, group is empty -> vacuously true.
        evaluator.Evaluate(matcher, MakeContext(tmp)).Should().BeTrue();
    }

    [TestMethod]
    public void NestedGroups_AndOfOr()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("a.mp4", new byte[] { 1 }); // video, but no image, no cover
        var evaluator = BuildEvaluator();

        var matcher = new ResourceMatcher
        {
            Combinator = SearchCombinator.And,
            Groups = new()
            {
                new ResourceMatcher
                {
                    Combinator = SearchCombinator.Or,
                    Leaves = new()
                    {
                        FileLeaf(MediaTypeFileCountPredicate.PredicateId, new MediaTypeFileCountParameters
                        {
                            MediaType = PredicateMediaType.Video,
                            Operator = ComparisonOperator.GreaterThan, Count = 0,
                        }),
                        FileLeaf(MediaTypeFileCountPredicate.PredicateId, new MediaTypeFileCountParameters
                        {
                            MediaType = PredicateMediaType.Audio,
                            Operator = ComparisonOperator.GreaterThan, Count = 0,
                        }),
                    },
                },
                new ResourceMatcher
                {
                    Combinator = SearchCombinator.And,
                    Leaves = new()
                    {
                        FileLeaf(HasCoverImagePredicate.PredicateId, null, negated: true),
                    },
                },
            },
        };

        evaluator.Evaluate(matcher, MakeContext(tmp)).Should().BeTrue();
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
