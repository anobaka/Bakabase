using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.DataCard.Abstractions.Models.Domain;
using Bakabase.Modules.DataCard.Abstractions.Services;
using Bakabase.Modules.DataCard.Models.Input;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Tests.Utils;
using Bootstrap.Models.Constants;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// DataCard 集成测试
///
/// 覆盖:
/// 1. 基于 Resource 属性值批量生成初始 DataCard (CreateInitialData)
/// 2. 通过 Resource 查找关联的 DataCard (GetAssociatedCards)
/// 3. 通过 DataCard 查找关联的 Resource (GetAssociatedResourceIds)
/// 4. 按 Identity 防重复创建 (Add dedup)
/// </summary>
[TestClass]
public class DataCardTests
{
    private IServiceProvider _sp = null!;
    private IDataCardService _cardService = null!;
    private IDataCardTypeService _typeService = null!;
    private ICustomPropertyService _propertyService = null!;
    private ICustomPropertyValueService _propertyValueService = null!;
    private IResourceService _resourceService = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _cardService = _sp.GetRequiredService<IDataCardService>();
        _typeService = _sp.GetRequiredService<IDataCardTypeService>();
        _propertyService = _sp.GetRequiredService<ICustomPropertyService>();
        _propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();
        _resourceService = _sp.GetRequiredService<IResourceService>();
    }

    private async Task<int> CreateTextProperty(string name)
    {
        var p = await _propertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = name,
            Type = PropertyType.SingleLineText
        });
        return p.Id;
    }

    /// <summary>
    /// 创建一个 SingleChoice 属性，返回 propertyId 以及各 option 的 UUID（按传入 label 顺序）。
    /// </summary>
    private async Task<(int PropertyId, string[] OptionUuids)> CreateSingleChoiceProperty(
        string name, params string[] labels)
    {
        var choices = labels.Select(l => new ChoiceOptions
        {
            Value = Guid.NewGuid().ToString("N"),
            Label = l
        }).ToList();
        var optionsJson = JsonSerializer.Serialize(
            new SingleChoicePropertyOptions { Choices = choices },
            JsonSerializerOptions.Web);
        var p = await _propertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = name,
            Type = PropertyType.SingleChoice,
            Options = optionsJson
        });
        return (p.Id, choices.Select(c => c.Value).ToArray());
    }

    private async Task<(int PropertyId, string[] OptionUuids)> CreateMultipleChoiceProperty(
        string name, params string[] labels)
    {
        var choices = labels.Select(l => new ChoiceOptions
        {
            Value = Guid.NewGuid().ToString("N"),
            Label = l
        }).ToList();
        var optionsJson = JsonSerializer.Serialize(
            new MultipleChoicePropertyOptions { Choices = choices },
            JsonSerializerOptions.Web);
        var p = await _propertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = name,
            Type = PropertyType.MultipleChoice,
            Options = optionsJson
        });
        return (p.Id, choices.Select(c => c.Value).ToArray());
    }

    private async Task<(int PropertyId, string[] TagUuids)> CreateTagsProperty(
        string name, params (string? Group, string Name)[] tagDefs)
    {
        var tags = tagDefs.Select(td => new TagsPropertyOptions.TagOptions(td.Group, td.Name)
        {
            Value = Guid.NewGuid().ToString("N")
        }).ToList();
        var optionsJson = JsonSerializer.Serialize(
            new TagsPropertyOptions { Tags = tags },
            JsonSerializerOptions.Web);
        var p = await _propertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = name,
            Type = PropertyType.Tags,
            Options = optionsJson
        });
        return (p.Id, tags.Select(t => t.Value).ToArray());
    }

    /// <summary>
    /// 创建一个 Multilevel 属性，树结构由传入的叶子路径定义（每个叶子路径形如 "A/B/C"）。
    /// 返回 propertyId 和一个按 label 路径 → UUID 路径的映射。
    /// </summary>
    private async Task<(int PropertyId, Dictionary<string, string[]> PathUuidsByLabelPath)>
        CreateMultilevelProperty(string name, params string[] leafPaths)
    {
        var root = new List<MultilevelDataOptions>();
        var pathMap = new Dictionary<string, string[]>();

        foreach (var leafPath in leafPaths)
        {
            var labels = leafPath.Split('/');
            var currentSiblings = root;
            var uuidPath = new List<string>();
            foreach (var label in labels)
            {
                var node = currentSiblings.FirstOrDefault(n => n.Label == label);
                if (node == null)
                {
                    node = new MultilevelDataOptions
                    {
                        Value = Guid.NewGuid().ToString("N"),
                        Label = label,
                        Color = string.Empty
                    };
                    currentSiblings.Add(node);
                }
                uuidPath.Add(node.Value);
                node.Children ??= [];
                currentSiblings = node.Children;

                // Record the prefix path label → uuid mapping at each depth.
                var prefixLabel = string.Join("/", labels.Take(uuidPath.Count));
                pathMap[prefixLabel] = uuidPath.ToArray();
            }
        }

        var optionsJson = JsonSerializer.Serialize(
            new MultilevelPropertyOptions { Data = root },
            JsonSerializerOptions.Web);
        var p = await _propertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = name,
            Type = PropertyType.Multilevel,
            Options = optionsJson
        });
        return (p.Id, pathMap);
    }

    /// <summary>ListString 序列化（与 DbValue 一致，用于比对）。</summary>
    private static string? SerializeList(params string[] values) =>
        new List<string>(values).SerializeAsStandardValue(StandardValueType.ListString);

    private async Task<int[]> CreateResources(int count)
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var dbModels = Enumerable.Range(0, count).Select(i => new ResourceDbModel
        {
            Path = $"/test/datacard/{marker}/r{i}",
            IsFile = true
        }).ToList();
        var added = await _resourceService.AddAll(dbModels);
        return added.Select(d => d.Id).ToArray();
    }

    private async Task AddResourcePropertyValue(int resourceId, int propertyId, string? value)
    {
        await _propertyValueService.AddDbModel(new CustomPropertyValueDbModel
        {
            ResourceId = resourceId,
            PropertyId = propertyId,
            Value = value,
            Scope = 0
        });
    }

    private async Task<int> CreateType(
        string name,
        int[] propertyIds,
        int[]? identityPropertyIds = null,
        DataCardMatchRules? matchRules = null)
    {
        var resp = await _typeService.Add(new DataCardTypeAddInputModel
        {
            Name = name,
            PropertyIds = propertyIds.ToList(),
            IdentityPropertyIds = (identityPropertyIds ?? propertyIds).ToList(),
            MatchRules = matchRules
        });
        resp.Data.Should().NotBeNull();
        return resp.Data!.Id;
    }

    private readonly record struct AddCardResult(int Code, int? CardId);

    private async Task<AddCardResult> AddCard(int typeId,
        params (int PropertyId, string? Value)[] values)
    {
        var resp = await _cardService.Add(new DataCardAddInputModel
        {
            TypeId = typeId,
            PropertyValues = values.Select(v => new DataCardPropertyValueInputModel
            {
                PropertyId = v.PropertyId,
                Value = v.Value
            }).ToList()
        });
        return new AddCardResult(resp.Code, resp.Data?.Id);
    }

    /// <summary>
    /// 场景：基于已有 Resource 属性值生成初始 DataCard，去重且幂等
    ///
    /// - 3 个 Resource 的 Author 为 Alice / Bob / Alice
    /// - Type.IdentityPropertyIds = [Author]
    /// - 第一次调用 → 建 2 张 Card (Alice, Bob)
    /// - 第二次调用 → 0 张（signature 已存在）
    /// </summary>
    [TestMethod]
    public async Task CreateInitialData_FromResourceValues_CreatesDistinctCardsAndIsIdempotent()
    {
        var authorId = await CreateTextProperty("Author");

        var resourceIds = await CreateResources(3);
        await AddResourcePropertyValue(resourceIds[0], authorId, "Alice");
        await AddResourcePropertyValue(resourceIds[1], authorId, "Bob");
        await AddResourcePropertyValue(resourceIds[2], authorId, "Alice");

        var typeId = await CreateType("AuthorCard", [authorId]);

        var firstResp = await _cardService.CreateInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        firstResp.Code.Should().Be((int) ResponseCode.Success);
        firstResp.Data.Should().Be(2);

        var cards = await _cardService.GetAll(x => x.TypeId == typeId);
        cards.Should().HaveCount(2);
        cards.SelectMany(c => c.PropertyValues ?? [])
            .Select(pv => pv.Value)
            .Should().BeEquivalentTo(["Alice", "Bob"]);

        // 第二次：identity signature 已存在，不应重复创建
        var secondResp = await _cardService.CreateInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        secondResp.Data.Should().Be(0);
        (await _cardService.GetAll(x => x.TypeId == typeId)).Should().HaveCount(2);
    }

    /// <summary>
    /// 场景：通过 Resource 查找关联的 DataCard
    ///
    /// - Type.MatchRules: AutoBindEnabled, MatchProperties=[Author], MatchMode=Any
    /// - Resource Author = "Alice"
    /// - 有 Alice / Bob 两张 Card
    /// - 期望 GetAssociatedCards 只命中 Alice
    /// </summary>
    [TestMethod]
    public async Task GetAssociatedCards_ByResource_ReturnsOnlyCardsWithMatchingValues()
    {
        var authorId = await CreateTextProperty("Author");

        var resourceIds = await CreateResources(1);
        await AddResourcePropertyValue(resourceIds[0], authorId, "Alice");

        var typeId = await CreateType("AuthorCard", [authorId],
            matchRules: new DataCardMatchRules
            {
                AutoBindEnabled = true,
                MatchProperties = [authorId],
                MatchMode = DataCardMatchMode.Any
            });

        var alice = await AddCard(typeId, (authorId, "Alice"));
        var bob = await AddCard(typeId, (authorId, "Bob"));
        alice.CardId.Should().NotBeNull();
        bob.CardId.Should().NotBeNull();

        var associated = await _cardService.GetAssociatedCards(resourceIds[0]);

        associated.Should().ContainSingle();
        associated[0].Id.Should().Be(alice.CardId!.Value);
    }

    /// <summary>
    /// 场景：通过 DataCard 查找关联的 Resource
    ///
    /// - R1/R2 Author = Alice, R3 Author = Bob
    /// - Card Author = Alice
    /// - 期望 GetAssociatedResourceIds 只返回 R1/R2
    /// </summary>
    [TestMethod]
    public async Task GetAssociatedResourceIds_ByCard_ReturnsOnlyResourcesWithMatchingValues()
    {
        var authorId = await CreateTextProperty("Author");

        var resourceIds = await CreateResources(3);
        await AddResourcePropertyValue(resourceIds[0], authorId, "Alice");
        await AddResourcePropertyValue(resourceIds[1], authorId, "Alice");
        await AddResourcePropertyValue(resourceIds[2], authorId, "Bob");

        var typeId = await CreateType("AuthorCard", [authorId],
            matchRules: new DataCardMatchRules
            {
                AutoBindEnabled = true,
                MatchProperties = [authorId],
                MatchMode = DataCardMatchMode.Any
            });

        var alice = await AddCard(typeId, (authorId, "Alice"));
        alice.CardId.Should().NotBeNull();

        var associatedResourceIds =
            await _cardService.GetAssociatedResourceIds(alice.CardId!.Value);

        associatedResourceIds.Should().BeEquivalentTo(new[] {resourceIds[0], resourceIds[1]});
    }

    /// <summary>
    /// 场景：PreviewInitialData 只计数，不落库
    ///
    /// - 3 个 Resource Author = Alice / Bob / Alice
    /// - 预演 → toCreate=2, alreadyExists=0；数据库无新增
    /// - 连续预演两次返回相同结果（幂等、只读）
    /// </summary>
    [TestMethod]
    public async Task PreviewInitialData_BeforeAnyCard_CountsToCreateAndIsIdempotent()
    {
        var authorId = await CreateTextProperty("Author");

        var resourceIds = await CreateResources(3);
        await AddResourcePropertyValue(resourceIds[0], authorId, "Alice");
        await AddResourcePropertyValue(resourceIds[1], authorId, "Bob");
        await AddResourcePropertyValue(resourceIds[2], authorId, "Alice");

        var typeId = await CreateType("AuthorCard", [authorId]);

        var first = await _cardService.PreviewInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        first.Code.Should().Be((int) ResponseCode.Success);
        first.Data.Should().NotBeNull();
        first.Data!.ToCreate.Should().Be(2);
        first.Data.AlreadyExists.Should().Be(0);

        // 没有真的建 Card
        (await _cardService.GetAll(x => x.TypeId == typeId)).Should().BeEmpty();

        // 再调一次结果一致
        var second = await _cardService.PreviewInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        second.Data!.ToCreate.Should().Be(2);
        second.Data.AlreadyExists.Should().Be(0);
    }

    /// <summary>
    /// 场景：部分 Card 已存在时 preview 能区分新/旧
    ///
    /// - 3 个 Resource Author = Alice / Bob / Charlie
    /// - 预先手动创建 Alice 卡
    /// - 预演 → toCreate=2 (Bob, Charlie), alreadyExists=1 (Alice)
    /// </summary>
    [TestMethod]
    public async Task PreviewInitialData_WithExistingCard_SeparatesNewFromExisting()
    {
        var authorId = await CreateTextProperty("Author");

        var resourceIds = await CreateResources(3);
        await AddResourcePropertyValue(resourceIds[0], authorId, "Alice");
        await AddResourcePropertyValue(resourceIds[1], authorId, "Bob");
        await AddResourcePropertyValue(resourceIds[2], authorId, "Charlie");

        var typeId = await CreateType("AuthorCard", [authorId]);

        // 先建一张 Alice 卡
        var aliceCard = await AddCard(typeId, (authorId, "Alice"));
        aliceCard.CardId.Should().NotBeNull();

        var preview = await _cardService.PreviewInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        preview.Data!.ToCreate.Should().Be(2);
        preview.Data.AlreadyExists.Should().Be(1);
    }

    /// <summary>
    /// 场景：preview 与 create 报告数字一致，且 create 后再 preview 显示全部已存在
    /// </summary>
    [TestMethod]
    public async Task PreviewInitialData_MatchesCreate_AndAfterCreateShowsAllExisting()
    {
        var authorId = await CreateTextProperty("Author");

        var resourceIds = await CreateResources(3);
        await AddResourcePropertyValue(resourceIds[0], authorId, "Alice");
        await AddResourcePropertyValue(resourceIds[1], authorId, "Bob");
        await AddResourcePropertyValue(resourceIds[2], authorId, "Alice");

        var typeId = await CreateType("AuthorCard", [authorId]);

        var before = await _cardService.PreviewInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        var expectedToCreate = before.Data!.ToCreate;
        expectedToCreate.Should().Be(2);

        var created = await _cardService.CreateInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        created.Data.Should().Be(expectedToCreate, "create 的结果应与 preview 一致");

        var after = await _cardService.PreviewInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        after.Data!.ToCreate.Should().Be(0);
        after.Data.AlreadyExists.Should().Be(expectedToCreate);
    }

    /// <summary>
    /// 场景：allowNullPropertyIds 的变化会改变 preview 结果
    ///
    /// 两个 identity 属性 (Author, Year)，两个 Resource 都只写 Author，没 Year：
    /// - 不允许空值 → Year 属性值域为空，返回 0
    /// - 允许 Year 为空 → Author({Alice, Bob}) × Year({null}) = 2 个组合
    /// </summary>
    [TestMethod]
    public async Task PreviewInitialData_AllowNullChange_ChangesResult()
    {
        var authorId = await CreateTextProperty("Author");
        var yearId = await CreateTextProperty("Year");

        var resourceIds = await CreateResources(2);
        await AddResourcePropertyValue(resourceIds[0], authorId, "Alice");
        await AddResourcePropertyValue(resourceIds[1], authorId, "Bob");
        // 两个 Resource 都不写 Year

        var typeId = await CreateType("AuthorYearCard", [authorId, yearId]);

        var strict = await _cardService.PreviewInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        strict.Data!.ToCreate.Should().Be(0, "Year 没任何值且不允许空时，组合为空");
        strict.Data.AlreadyExists.Should().Be(0);

        var withNullYear = await _cardService.PreviewInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: [yearId]);
        withNullYear.Data!.ToCreate.Should().Be(2,
            "允许 Year 空值后: 2 个 Author × {null} = 2 种组合");
    }

    /// <summary>
    /// 场景：onlyFromResources=false 时，SingleChoice 属性所有 options 都应计入候选
    ///
    /// - SingleChoice "Category" 有 3 个 options: A / B / C
    /// - 只有 1 个 Resource 使用了 A
    /// - onlyFromResources=true  → resource 里的 A 与 options 交集 → toCreate=1
    /// - onlyFromResources=false → A、B、C 全进候选 → toCreate=3
    /// - create 也应当遵守同一逻辑
    /// </summary>
    [TestMethod]
    public async Task PreviewAndCreate_OnlyFromResourcesFalse_IncludesUnusedSingleChoiceOptions()
    {
        var (categoryId, uuids) = await CreateSingleChoiceProperty("Category", "A", "B", "C");

        var resourceIds = await CreateResources(1);
        await AddResourcePropertyValue(resourceIds[0], categoryId, uuids[0]); // Only A used

        var typeId = await CreateType("CategoryCard", [categoryId]);

        // onlyFromResources=true → 只计一个
        var previewOnly = await _cardService.PreviewInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        previewOnly.Data!.ToCreate.Should().Be(1,
            "只使用 resource 值时，仅 A 在候选里");

        // onlyFromResources=false → 应囊括所有 option
        var previewAll = await _cardService.PreviewInitialData(typeId, onlyFromResources: false,
            allowNullPropertyIds: null);
        previewAll.Data!.ToCreate.Should().Be(3,
            "不限制时 A/B/C 都应进候选");

        // 实际 create 也要走同一套逻辑
        var created = await _cardService.CreateInitialData(typeId, onlyFromResources: false,
            allowNullPropertyIds: null);
        created.Data.Should().Be(3, "create 的数量应与 preview 一致");

        var cards = await _cardService.GetAll(x => x.TypeId == typeId);
        cards.Should().HaveCount(3);
        cards.SelectMany(c => c.PropertyValues ?? [])
            .Select(pv => pv.Value)
            .Should().BeEquivalentTo(uuids);
    }

    /// <summary>
    /// 场景：onlyFromResources=false 对 MultipleChoice 也应展开 options，每个 option 一张卡
    /// - MultipleChoice "Genre" 有 3 options: X / Y / Z
    /// - 1 个 Resource 使用了 [X]
    /// - true  → 只 1 个候选 (X)
    /// - false → 3 个候选，pv.Value 分别为 X / Y / Z 的 UUID（无组合）
    /// </summary>
    [TestMethod]
    public async Task PreviewAndCreate_OnlyFromResourcesFalse_IncludesUnusedMultipleChoiceOptions()
    {
        var (genreId, uuids) = await CreateMultipleChoiceProperty("Genre", "X", "Y", "Z");

        var resourceIds = await CreateResources(1);
        await AddResourcePropertyValue(resourceIds[0], genreId, SerializeList(uuids[0]));

        var typeId = await CreateType("GenreCard", [genreId]);

        var onlyPreview = await _cardService.PreviewInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        onlyPreview.Data!.ToCreate.Should().Be(1);

        var fullPreview = await _cardService.PreviewInitialData(typeId, onlyFromResources: false,
            allowNullPropertyIds: null);
        fullPreview.Data!.ToCreate.Should().Be(3,
            "每个 option 独立成候选");

        var created = await _cardService.CreateInitialData(typeId, onlyFromResources: false,
            allowNullPropertyIds: null);
        created.Data.Should().Be(3);

        var cards = await _cardService.GetAll(x => x.TypeId == typeId);
        cards.Should().HaveCount(3);
        cards.SelectMany(c => c.PropertyValues ?? [])
            .Select(pv => pv.Value)
            .Should().BeEquivalentTo(uuids);
    }

    /// <summary>
    /// 场景：onlyFromResources=false 对 Tags 展开所有 tag，每个 tag 一张卡，pv.Value 为 tag UUID
    /// - Tags "Keywords" 配置 3 个 tag
    /// - 没有 resource 引用
    /// - true  → 0
    /// - false → 3，pv.Value 为每个 tag 的 UUID（无组合）
    /// </summary>
    [TestMethod]
    public async Task PreviewAndCreate_OnlyFromResourcesFalse_IncludesAllTagOptionsAsUuid()
    {
        var (keywordsId, tagUuids) = await CreateTagsProperty("Keywords",
            (null, "k1"), ("g", "k2"), (null, "k3"));

        var typeId = await CreateType("TagCard", [keywordsId]);

        var onlyPreview = await _cardService.PreviewInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        onlyPreview.Data!.ToCreate.Should().Be(0,
            "没 resource 引用 & 不允许 null，没候选");

        var fullPreview = await _cardService.PreviewInitialData(typeId, onlyFromResources: false,
            allowNullPropertyIds: null);
        fullPreview.Data!.ToCreate.Should().Be(3);

        var created = await _cardService.CreateInitialData(typeId, onlyFromResources: false,
            allowNullPropertyIds: null);
        created.Data.Should().Be(3);

        var cards = await _cardService.GetAll(x => x.TypeId == typeId);
        cards.SelectMany(c => c.PropertyValues ?? [])
            .Select(pv => pv.Value)
            .Should().BeEquivalentTo(tagUuids);
    }

    /// <summary>
    /// 场景：onlyFromResources=false 对 Multilevel，只展开叶子节点，值是叶子 UUID（单项 list 序列化）
    /// - 树: Tech/Programming/Python, Tech/Programming/CSharp, Tech/Database
    /// - 中间节点 (Tech, Tech/Programming) 不计
    /// - true → 0
    /// - false → 3，每条 pv.Value 是对应叶子节点 UUID 的单项 list 序列化
    /// </summary>
    [TestMethod]
    public async Task PreviewInitialData_OnlyFromResourcesFalse_ExpandsMultilevelLeafUuids()
    {
        var (multilevelId, pathMap) = await CreateMultilevelProperty(
            "Category",
            "Tech/Programming/Python",
            "Tech/Programming/CSharp",
            "Tech/Database");

        var typeId = await CreateType("CategoryCard", [multilevelId]);

        var onlyPreview = await _cardService.PreviewInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        onlyPreview.Data!.ToCreate.Should().Be(0);

        var fullPreview = await _cardService.PreviewInitialData(typeId, onlyFromResources: false,
            allowNullPropertyIds: null);
        fullPreview.Data!.ToCreate.Should().Be(3,
            "只有 3 个叶子节点，中间节点 (Tech, Tech/Programming) 不计");

        var created = await _cardService.CreateInitialData(typeId, onlyFromResources: false,
            allowNullPropertyIds: null);
        created.Data.Should().Be(3);

        var leafUuids = new[]
        {
            pathMap["Tech/Programming/Python"][^1],
            pathMap["Tech/Programming/CSharp"][^1],
            pathMap["Tech/Database"][^1],
        };
        var cards = await _cardService.GetAll(x => x.TypeId == typeId);
        cards.SelectMany(c => c.PropertyValues ?? [])
            .Select(pv => pv.Value)
            .Should().BeEquivalentTo(leafUuids);
    }

    /// <summary>
    /// 场景：onlyFromResources=true + 多选类型会把资源里的 list 展开成单个 UUID，每个 UUID 独立成卡
    /// - Tags 配置 3 个 tag，UUID 为 [T0, T1, T2]
    /// - 1 个 resource 的 tag 值为 [T0, T1]（序列化 list）
    /// - true → 2 张卡 (T0, T1)；T2 未被使用不计
    /// </summary>
    [TestMethod]
    public async Task CreateInitialData_OnlyFromResourcesTrue_ExpandsMultiSelectValues()
    {
        var (keywordsId, tagUuids) = await CreateTagsProperty("Keywords",
            (null, "t0"), (null, "t1"), (null, "t2"));

        var resourceIds = await CreateResources(1);
        await AddResourcePropertyValue(resourceIds[0], keywordsId, SerializeList(tagUuids[0], tagUuids[1]));

        var typeId = await CreateType("TagCard", [keywordsId]);

        var created = await _cardService.CreateInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        created.Data.Should().Be(2, "资源里的 [T0,T1] 应展开成 T0、T1 两张卡");

        var cards = await _cardService.GetAll(x => x.TypeId == typeId);
        cards.SelectMany(c => c.PropertyValues ?? [])
            .Select(pv => pv.Value)
            .Should().BeEquivalentTo(new[] {tagUuids[0], tagUuids[1]});
    }

    /// <summary>
    /// 场景：onlyFromResources=true 的资源值要与当前 options 求交集，过期 UUID 被丢弃
    /// - SingleChoice 配置 A、B 两个 option
    /// - 2 个 resource：一个用 A（合法），另一个用一个过期 UUID（options 中已不存在）
    /// - true → 1 张卡（只剩 A）
    /// </summary>
    [TestMethod]
    public async Task CreateInitialData_OnlyFromResourcesTrue_FiltersExpiredUuidsAgainstOptions()
    {
        var (categoryId, uuids) = await CreateSingleChoiceProperty("Category", "A", "B");
        var expiredUuid = Guid.NewGuid().ToString("N");

        var resourceIds = await CreateResources(2);
        await AddResourcePropertyValue(resourceIds[0], categoryId, uuids[0]);
        await AddResourcePropertyValue(resourceIds[1], categoryId, expiredUuid);

        var typeId = await CreateType("CategoryCard", [categoryId]);

        var created = await _cardService.CreateInitialData(typeId, onlyFromResources: true,
            allowNullPropertyIds: null);
        created.Data.Should().Be(1, "过期 UUID 应被交集过滤，仅 A 留下");

        var cards = await _cardService.GetAll(x => x.TypeId == typeId);
        cards.SelectMany(c => c.PropertyValues ?? [])
            .Select(pv => pv.Value)
            .Should().BeEquivalentTo(new[] {uuids[0]});
    }

    /// <summary>
    /// 场景：onlyFromResources=false 完全忽略资源数据，只用 options
    /// - SingleChoice 配置 A、B
    /// - resource 写入一个过期 UUID（options 中已不存在）
    /// - false → 2 张卡（A、B），过期 UUID 不应出现
    /// </summary>
    [TestMethod]
    public async Task CreateInitialData_OnlyFromResourcesFalse_IgnoresResourceDataForReferenceTypes()
    {
        var (categoryId, uuids) = await CreateSingleChoiceProperty("Category", "A", "B");
        var expiredUuid = Guid.NewGuid().ToString("N");

        var resourceIds = await CreateResources(1);
        await AddResourcePropertyValue(resourceIds[0], categoryId, expiredUuid);

        var typeId = await CreateType("CategoryCard", [categoryId]);

        var created = await _cardService.CreateInitialData(typeId, onlyFromResources: false,
            allowNullPropertyIds: null);
        created.Data.Should().Be(2);

        var cards = await _cardService.GetAll(x => x.TypeId == typeId);
        cards.SelectMany(c => c.PropertyValues ?? [])
            .Select(pv => pv.Value)
            .Should().BeEquivalentTo(uuids);
    }

    /// <summary>
    /// 场景：按 Identity 防重复创建
    ///
    /// - Type.IdentityPropertyIds = [Author]
    /// - 第一次 Add(Author=Alice) → 成功
    /// - 第二次 Add(Author=Alice) → BadRequest
    /// - Add(Author=Bob) → 成功
    /// - FindByIdentity(Author=Alice) → 命中第一次创建的那张
    /// </summary>
    [TestMethod]
    public async Task Add_DuplicateIdentityValues_ReturnsBadRequestAndFindsExisting()
    {
        var authorId = await CreateTextProperty("Author");
        var typeId = await CreateType("AuthorCard", [authorId]);

        var first = await AddCard(typeId, (authorId, "Alice"));
        first.Code.Should().Be((int) ResponseCode.Success);
        first.CardId.Should().NotBeNull();

        var duplicate = await AddCard(typeId, (authorId, "Alice"));
        duplicate.Code.Should().Be((int) ResponseCode.InvalidPayloadOrOperation,
            "相同 identity 值应被拒绝");
        duplicate.CardId.Should().BeNull();

        var different = await AddCard(typeId, (authorId, "Bob"));
        different.Code.Should().Be((int) ResponseCode.Success);
        different.CardId.Should().NotBeNull();
        different.CardId.Should().NotBe(first.CardId);

        (await _cardService.GetAll(x => x.TypeId == typeId)).Should().HaveCount(2);

        var found = await _cardService.FindByIdentity(new DataCardFindByIdentityInputModel
        {
            TypeId = typeId,
            PropertyValues =
            [
                new DataCardPropertyValueInputModel {PropertyId = authorId, Value = "Alice"}
            ]
        });
        found.Data.Should().NotBeNull();
        found.Data!.Id.Should().Be(first.CardId!.Value);
    }
}
