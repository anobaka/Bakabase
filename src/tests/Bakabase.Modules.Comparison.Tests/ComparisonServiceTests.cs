using System.Reflection;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.Modules.Comparison.Abstractions.Services;
using Bakabase.Modules.Comparison.Models.Domain;
using Bakabase.Modules.Comparison.Models.Domain.Constants;
using Bakabase.Modules.Comparison.Models.Input;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Modules.Comparison.Tests;

/// <summary>
/// Integration coverage for ComparisonService: plan CRUD, the public
/// CalculateSimilarity scoring contract (weights, veto, null-value behaviors),
/// and end-to-end ExecuteComparisonAsync grouping over path-mark-synced resources.
/// </summary>
[TestClass]
public sealed class ComparisonServiceTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;
    private IComparisonService _service = null!;
    private const double Delta = 1e-9;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _service = _sp.GetRequiredService<IComparisonService>();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"ComparisonServiceTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    /// <summary>
    /// Creates directory resources two layers deep (testRoot/{parent}/{name}) via a
    /// layer-2 resource path mark + sync. Distinct parents let two resources share a
    /// filename, which is what the comparison-by-filename tests rely on.
    /// </summary>
    private async Task SeedResources(params (string Parent, string[] Names)[] groups)
    {
        foreach (var (parent, names) in groups)
        {
            foreach (var name in names)
            {
                Directory.CreateDirectory(Path.Combine(_testRoot, parent, name));
            }
        }

        await _sp.GetRequiredService<IPathMarkService>().Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 2,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });

        await _sp.GetRequiredService<ResourceSyncService>().SyncResources(
            ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);
    }

    private static Resource Res(string path, DateTime? playedAt = null)
        => new() { Path = path, PlayedAt = playedAt };

    private static ComparisonPlan Plan(params ComparisonRule[] rules)
        => new() { Threshold = 80, Rules = rules.ToList() };

    private static ComparisonRule Rule(
        InternalProperty property,
        ComparisonMode mode,
        int weight = 1,
        int order = 0,
        bool isVeto = false,
        double vetoThreshold = 1.0,
        NullValueBehavior bothNull = NullValueBehavior.Skip,
        NullValueBehavior oneNull = NullValueBehavior.Skip)
        => new()
        {
            Order = order,
            PropertyPool = PropertyPool.Internal,
            PropertyId = (int)property,
            Mode = mode,
            Weight = weight,
            IsVeto = isVeto,
            VetoThreshold = vetoThreshold,
            BothNullBehavior = bothNull,
            OneNullBehavior = oneNull
        };

    private static ComparisonRuleInputModel FilenameRuleInput(int order = 0, int weight = 1)
        => new()
        {
            Order = order,
            PropertyPool = PropertyPool.Internal,
            PropertyId = (int)InternalProperty.Filename,
            Mode = ComparisonMode.StrictEqual,
            Weight = weight
        };

    private Task ExecutePlan(int planId) => _service.ExecuteComparisonAsync(
        planId, (_, _) => Task.CompletedTask, new PauseToken(), CancellationToken.None);

    #region Plan CRUD

    [TestMethod]
    public async Task CreatePlan_PersistsNameThresholdAndRules()
    {
        var plan = await _service.CreatePlanAsync(new ComparisonPlanCreateInputModel
        {
            Name = "dedupe",
            Threshold = 65,
            Rules = [FilenameRuleInput(0), FilenameRuleInput(1)]
        });

        Assert.IsTrue(plan.Id > 0);
        var fetched = await _service.GetPlanDbModelAsync(plan.Id);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("dedupe", fetched!.Value.Plan.Name);
        Assert.AreEqual(65, fetched.Value.Plan.Threshold, Delta);
        Assert.AreEqual(2, fetched.Value.Rules.Count);
    }

    [TestMethod]
    public async Task GetPlanDbModel_NonExistent_ReturnsNull()
        => Assert.IsNull(await _service.GetPlanDbModelAsync(999999));

    [TestMethod]
    public async Task GetAllPlanDbModels_ReturnsEveryCreatedPlan()
    {
        await _service.CreatePlanAsync(new ComparisonPlanCreateInputModel { Name = "p1", Rules = [FilenameRuleInput()] });
        await _service.CreatePlanAsync(new ComparisonPlanCreateInputModel { Name = "p2", Rules = [FilenameRuleInput()] });
        await _service.CreatePlanAsync(new ComparisonPlanCreateInputModel { Name = "p3", Rules = [FilenameRuleInput()] });

        Assert.AreEqual(3, (await _service.GetAllPlanDbModelsAsync()).Count);
    }

    [TestMethod]
    public async Task UpdatePlan_ChangesNameAndThreshold_LeavesRulesUntouchedWhenNull()
    {
        var plan = await _service.CreatePlanAsync(new ComparisonPlanCreateInputModel
        {
            Name = "before", Threshold = 50, Rules = [FilenameRuleInput()]
        });

        await _service.UpdatePlanAsync(plan.Id, new ComparisonPlanPatchInputModel
        {
            Name = "after", Threshold = 90
        });

        var fetched = await _service.GetPlanDbModelAsync(plan.Id);
        Assert.AreEqual("after", fetched!.Value.Plan.Name);
        Assert.AreEqual(90, fetched.Value.Plan.Threshold, Delta);
        Assert.AreEqual(1, fetched.Value.Rules.Count);
    }

    [TestMethod]
    public async Task UpdatePlan_NonNullRules_ReplacesEntireRuleSet()
    {
        var plan = await _service.CreatePlanAsync(new ComparisonPlanCreateInputModel
        {
            Name = "p", Rules = [FilenameRuleInput(0), FilenameRuleInput(1), FilenameRuleInput(2)]
        });

        await _service.UpdatePlanAsync(plan.Id, new ComparisonPlanPatchInputModel
        {
            Rules = [FilenameRuleInput(0)]
        });

        Assert.AreEqual(1, (await _service.GetPlanDbModelAsync(plan.Id))!.Value.Rules.Count);
    }

    [TestMethod]
    public async Task DeletePlan_RemovesPlan()
    {
        var plan = await _service.CreatePlanAsync(new ComparisonPlanCreateInputModel
        {
            Name = "doomed", Rules = [FilenameRuleInput()]
        });

        await _service.DeletePlanAsync(plan.Id);
        Assert.IsNull(await _service.GetPlanDbModelAsync(plan.Id));
    }

    [TestMethod]
    public async Task DuplicatePlan_CopiesRulesAndMarksCopy()
    {
        var plan = await _service.CreatePlanAsync(new ComparisonPlanCreateInputModel
        {
            Name = "original", Threshold = 70, Rules = [FilenameRuleInput(0), FilenameRuleInput(1)]
        });

        var copy = await _service.DuplicatePlanAsync(plan.Id);
        Assert.AreNotEqual(plan.Id, copy.Id);
        Assert.AreEqual("original (Copy)", copy.Name);
        Assert.AreEqual(70, copy.Threshold, Delta);
        Assert.AreEqual(2, copy.Rules.Count);
        Assert.AreEqual(2, (await _service.GetAllPlanDbModelsAsync()).Count);
    }

    #endregion

    #region CalculateSimilarity

    [TestMethod]
    public void CalculateSimilarity_IdenticalFilename_Returns100()
        => Assert.AreEqual(100, _service.CalculateSimilarity(
            Res("/lib/Title"), Res("/other/Title"),
            Plan(Rule(InternalProperty.Filename, ComparisonMode.StrictEqual))), Delta);

    [TestMethod]
    public void CalculateSimilarity_DifferentFilename_ReturnsZero()
        => Assert.AreEqual(0, _service.CalculateSimilarity(
            Res("/lib/Alpha"), Res("/lib/Beta"),
            Plan(Rule(InternalProperty.Filename, ComparisonMode.StrictEqual))), Delta);

    [TestMethod]
    public void CalculateSimilarity_TextSimilarity_ScoresPartialOverlap()
        // char sets {a,b,c} vs {a,b,d}: Jaccard 2/4 -> 0.5 -> 50%.
        => Assert.AreEqual(50, _service.CalculateSimilarity(
            Res("/lib/abc"), Res("/lib/abd"),
            Plan(Rule(InternalProperty.Filename, ComparisonMode.TextSimilarity))), Delta);

    [TestMethod]
    public void CalculateSimilarity_WeightedRules_AverageByWeight()
    {
        // Filename matches (weight 3), directory differs (weight 1): 3/4 -> 75%.
        var score = _service.CalculateSimilarity(
            Res("/libA/Same"), Res("/libB/Same"),
            Plan(
                Rule(InternalProperty.Filename, ComparisonMode.StrictEqual, weight: 3, order: 0),
                Rule(InternalProperty.DirectoryPath, ComparisonMode.StrictEqual, weight: 1, order: 1)));
        Assert.AreEqual(75, score, Delta);
    }

    [TestMethod]
    public void CalculateSimilarity_VetoRuleBelowThreshold_ZerosWholeScore()
    {
        // The veto rule (filename) fails -> 0 regardless of the matching directory rule.
        var score = _service.CalculateSimilarity(
            Res("/lib/Alpha"), Res("/lib/Beta"),
            Plan(
                Rule(InternalProperty.Filename, ComparisonMode.StrictEqual, isVeto: true, order: 0),
                Rule(InternalProperty.DirectoryPath, ComparisonMode.StrictEqual, order: 1)));
        Assert.AreEqual(0, score, Delta);
    }

    [TestMethod]
    public void CalculateSimilarity_VetoRuleMet_DoesNotZeroScore()
    {
        // The veto rule passes -> normal weighted scoring applies.
        var score = _service.CalculateSimilarity(
            Res("/lib/Same"), Res("/lib/Same"),
            Plan(
                Rule(InternalProperty.Filename, ComparisonMode.StrictEqual, isVeto: true, order: 0),
                Rule(InternalProperty.DirectoryPath, ComparisonMode.StrictEqual, order: 1)));
        Assert.AreEqual(100, score, Delta);
    }

    [TestMethod]
    public void CalculateSimilarity_BothNullSkip_OnlyRuleSkipped_ReturnsZero()
        // PlayedAt is null on both, the sole rule is skipped -> no scorable rule -> 0.
        => Assert.AreEqual(0, _service.CalculateSimilarity(
            Res("/lib/A"), Res("/lib/B"),
            Plan(Rule(InternalProperty.PlayedAt, ComparisonMode.StrictEqual,
                bothNull: NullValueBehavior.Skip))), Delta);

    [TestMethod]
    public void CalculateSimilarity_BothNullPassVsFail_DiffersByBehavior()
    {
        var pass = Plan(
            Rule(InternalProperty.Filename, ComparisonMode.StrictEqual, order: 0),
            Rule(InternalProperty.PlayedAt, ComparisonMode.StrictEqual, order: 1,
                bothNull: NullValueBehavior.Pass));
        var fail = Plan(
            Rule(InternalProperty.Filename, ComparisonMode.StrictEqual, order: 0),
            Rule(InternalProperty.PlayedAt, ComparisonMode.StrictEqual, order: 1,
                bothNull: NullValueBehavior.Fail));

        // Filename matches; the null PlayedAt rule counts as a match (Pass) or a miss (Fail).
        Assert.AreEqual(100, _service.CalculateSimilarity(Res("/lib/X"), Res("/lib/X"), pass), Delta);
        Assert.AreEqual(50, _service.CalculateSimilarity(Res("/lib/X"), Res("/lib/X"), fail), Delta);
    }

    [TestMethod]
    public void CalculateSimilarity_OneNullFail_CountsAsMissNotVeto()
    {
        // One resource has PlayedAt, the other does not: OneNull=Fail adds weight to the
        // denominator only. The filename rule still matches -> 1/2 -> 50%.
        var score = _service.CalculateSimilarity(
            Res("/lib/X", DateTime.Now), Res("/lib/X"),
            Plan(
                Rule(InternalProperty.Filename, ComparisonMode.StrictEqual, order: 0),
                Rule(InternalProperty.PlayedAt, ComparisonMode.StrictEqual, order: 1,
                    oneNull: NullValueBehavior.Fail)));
        Assert.AreEqual(50, score, Delta);
    }

    #endregion

    #region ExecuteComparison

    [TestMethod]
    public async Task Execute_GroupsResourcesWithIdenticalFilename()
    {
        // Two "Alpha" directories under different parents -> one duplicate pair.
        await SeedResources(("g1", ["Alpha", "Beta"]), ("g2", ["Alpha", "Gamma"]));

        var plan = await _service.CreatePlanAsync(new ComparisonPlanCreateInputModel
        {
            Name = "by-name", Threshold = 50, Rules = [FilenameRuleInput()]
        });
        await ExecutePlan(plan.Id);

        var results = await _service.SearchResultGroupsAsync(plan.Id, new ComparisonResultSearchInputModel());
        Assert.AreEqual(1, results.Data!.Count);
        Assert.AreEqual(2, results.Data![0].MemberCount);

        var memberIds = await _service.GetResultGroupResourceIdsAsync(results.Data![0].Id);
        Assert.AreEqual(2, memberIds.Count);
        Assert.AreEqual(2, memberIds.Distinct().Count());
    }

    [TestMethod]
    public async Task Execute_NoDuplicateFilenames_ProducesNoGroups()
    {
        await SeedResources(("g1", ["A", "B", "C"]));

        var plan = await _service.CreatePlanAsync(new ComparisonPlanCreateInputModel
        {
            Name = "by-name", Threshold = 50, Rules = [FilenameRuleInput()]
        });
        await ExecutePlan(plan.Id);

        var results = await _service.SearchResultGroupsAsync(plan.Id, new ComparisonResultSearchInputModel());
        Assert.AreEqual(0, results.Data!.Count);
    }

    [TestMethod]
    public async Task Execute_SetsLastRunAt()
    {
        await SeedResources(("g1", ["A", "B"]));

        var plan = await _service.CreatePlanAsync(new ComparisonPlanCreateInputModel
        {
            Name = "p", Threshold = 50, Rules = [FilenameRuleInput()]
        });
        Assert.IsNull((await _service.GetPlanDbModelAsync(plan.Id))!.Value.Plan.LastRunAt);

        await ExecutePlan(plan.Id);

        Assert.IsNotNull((await _service.GetPlanDbModelAsync(plan.Id))!.Value.Plan.LastRunAt);
    }

    [TestMethod]
    public async Task Execute_ClearResults_RemovesGroups()
    {
        await SeedResources(("g1", ["Dup"]), ("g2", ["Dup"]));

        var plan = await _service.CreatePlanAsync(new ComparisonPlanCreateInputModel
        {
            Name = "p", Threshold = 50, Rules = [FilenameRuleInput()]
        });
        await ExecutePlan(plan.Id);
        Assert.AreEqual(1,
            (await _service.SearchResultGroupsAsync(plan.Id, new ComparisonResultSearchInputModel())).Data!.Count);

        await _service.ClearResultsAsync(plan.Id);
        Assert.AreEqual(0,
            (await _service.SearchResultGroupsAsync(plan.Id, new ComparisonResultSearchInputModel())).Data!.Count);
    }

    [TestMethod]
    public async Task Execute_FewerThanTwoResources_ProducesNoGroups()
    {
        await SeedResources(("g1", ["Only"]));

        var plan = await _service.CreatePlanAsync(new ComparisonPlanCreateInputModel
        {
            Name = "p", Threshold = 50, Rules = [FilenameRuleInput()]
        });
        await ExecutePlan(plan.Id);

        var results = await _service.SearchResultGroupsAsync(plan.Id, new ComparisonResultSearchInputModel());
        Assert.AreEqual(0, results.Data!.Count);
    }

    #endregion
}
