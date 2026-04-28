using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataCard.Abstractions.Models.Db;
using Bakabase.Modules.DataCard.Abstractions.Models.Domain;
using Bakabase.Modules.DataCard.Abstractions.Services;
using Bakabase.Modules.DataCard.Extensions;
using Bakabase.Modules.DataCard.Models.Input;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using DomainDataCard = Bakabase.Modules.DataCard.Abstractions.Models.Domain.DataCard;

namespace Bakabase.Modules.DataCard.Services;

public class DataCardService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, DataCardDbModel, int> orm,
    FullMemoryCacheResourceService<TDbContext, DataCardPropertyValueDbModel, int> pvOrm,
    IDataCardTypeService typeService,
    ICustomPropertyService customPropertyService,
    ICustomPropertyValueService cpvService) : IDataCardService
    where TDbContext : DbContext
{
    private static readonly Regex NameTemplateTokenRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    public async Task<List<DomainDataCard>> GetAll(Expression<Func<DataCardDbModel, bool>>? selector = null)
    {
        var dbModels = await orm.GetAll(selector);
        var cards = dbModels.Select(d => d.ToDomainModel()).ToList();
        await PopulatePropertyValues(cards);
        await ApplyNameTemplates(cards);
        return cards;
    }

    public async Task<DomainDataCard?> GetById(int id)
    {
        var db = await orm.GetByKey(id);
        if (db == null) return null;
        var card = db.ToDomainModel();
        await PopulatePropertyValues([card]);
        await ApplyNameTemplates([card]);
        return card;
    }

    public async Task<SearchResponse<DomainDataCard>> Search(DataCardSearchInputModel model)
    {
        Expression<Func<DataCardDbModel, bool>>? exp = null;
        if (model.TypeId.HasValue)
        {
            exp = exp.And(x => x.TypeId == model.TypeId.Value);
        }

        var allCards = await orm.GetAll(exp);

        if (!string.IsNullOrEmpty(model.Keyword) && allCards.Count > 0)
        {
            var matchedCardIds = await FilterCardIdsByKeyword(allCards.Select(c => c.Id), model.Keyword);
            allCards = allCards.Where(c => matchedCardIds.Contains(c.Id)).ToList();
        }

        var total = allCards.Count;
        var paged = allCards
            .OrderByDescending(x => x.UpdatedAt)
            .Skip((model.PageIndex - 1) * model.PageSize)
            .Take(model.PageSize)
            .Select(d => d.ToDomainModel())
            .ToList();

        await PopulatePropertyValues(paged);
        await ApplyNameTemplates(paged);
        return model.BuildResponse(paged, total);
    }

    private async Task<HashSet<int>> FilterCardIdsByKeyword(IEnumerable<int> candidateCardIds, string keyword)
    {
        var cardIds = candidateCardIds.ToHashSet();
        if (cardIds.Count == 0) return [];

        var allPvs = await pvOrm.GetAll(pv => cardIds.Contains(pv.CardId));
        if (allPvs.Count == 0) return [];

        var propIds = allPvs.Select(pv => pv.PropertyId).Distinct().ToList();
        var properties = (await customPropertyService.GetByKeys(propIds))
            .ToDictionary(p => p.Id, p => p);

        var filterByPropId =
            new Dictionary<int, (ResourceSearchFilter Filter, IPropertySearchHandler Handler,
                StandardValueType DbType)>();
        foreach (var (pid, cp) in properties)
        {
            var p = cp.ToProperty();
            var psh = PropertySystem.Property.TryGetSearchHandler(p.Type);
            if (psh == null) continue;
            var filter = psh.BuildSearchFilterByKeyword(p, keyword);
            if (filter == null) continue;
            filterByPropId[pid] = (filter, psh, p.Type.GetDbValueType());
        }

        if (filterByPropId.Count == 0) return [];

        var matched = new HashSet<int>();
        foreach (var pv in allPvs)
        {
            if (matched.Contains(pv.CardId)) continue;
            if (!filterByPropId.TryGetValue(pv.PropertyId, out var f)) continue;
            var dbValue = pv.Value?.DeserializeAsStandardValue(f.DbType);
            if (f.Handler.IsMatch(dbValue, f.Filter.Operation, f.Filter.DbValue))
            {
                matched.Add(pv.CardId);
            }
        }

        return matched;
    }

    public async Task<SingletonResponse<DomainDataCard>> Add(DataCardAddInputModel model)
    {
        var type = await typeService.GetById(model.TypeId);
        if (type != null)
        {
            var identityPropertyIds = GetEffectiveIdentityPropertyIds(type);
            if (identityPropertyIds?.Any() == true)
            {
                var duplicateCardId = await FindDuplicateCardId(type.Id, identityPropertyIds,
                    model.PropertyValues, excludeCardId: null);
                if (duplicateCardId.HasValue)
                {
                    return SingletonResponseBuilder<DomainDataCard>.BuildBadRequest(
                        "dataCard.error.duplicateIdentity");
                }
            }
        }

        var now = DateTime.Now;
        var db = new DataCardDbModel
        {
            TypeId = model.TypeId,
            CreatedAt = now,
            UpdatedAt = now
        };
        db = (await orm.Add(db)).Data;

        if (model.PropertyValues?.Any() == true)
        {
            var pvDbModels = model.PropertyValues.Select(pv => new DataCardPropertyValueDbModel
            {
                CardId = db.Id,
                PropertyId = pv.PropertyId,
                Value = pv.Value,
                Scope = pv.Scope
            }).ToList();
            await pvOrm.AddRange(pvDbModels);
        }

        var card = db.ToDomainModel();
        await PopulatePropertyValues([card]);
        await ApplyNameTemplates([card]);
        return new SingletonResponse<DomainDataCard>(card);
    }

    public async Task<BaseResponse> Update(int id, DataCardUpdateInputModel model)
    {
        var db = await orm.GetByKey(id);
        if (db == null)
        {
            return BaseResponseBuilder.NotFound;
        }

        db.UpdatedAt = DateTime.Now;
        await orm.Update(db);

        if (model.PropertyValues != null)
        {
            await pvOrm.RemoveAll(x => x.CardId == id);

            if (model.PropertyValues.Any())
            {
                var pvDbModels = model.PropertyValues.Select(pv => new DataCardPropertyValueDbModel
                {
                    CardId = id,
                    PropertyId = pv.PropertyId,
                    Value = pv.Value,
                    Scope = pv.Scope
                }).ToList();
                await pvOrm.AddRange(pvDbModels);
            }
        }

        return BaseResponseBuilder.Ok;
    }

    public async Task<BaseResponse> Delete(int id)
    {
        await pvOrm.RemoveAll(x => x.CardId == id);
        return await orm.RemoveByKey(id);
    }

    public async Task<SingletonResponse<DomainDataCard>> FindByIdentity(DataCardFindByIdentityInputModel model)
    {
        var type = await typeService.GetById(model.TypeId);
        if (type == null)
        {
            return new SingletonResponse<DomainDataCard>(data: null);
        }

        var identityPropertyIds = GetEffectiveIdentityPropertyIds(type);
        if (identityPropertyIds == null || !identityPropertyIds.Any())
        {
            return new SingletonResponse<DomainDataCard>(data: null);
        }

        var duplicateCardId = await FindDuplicateCardId(model.TypeId, identityPropertyIds,
            model.PropertyValues, model.ExcludeCardId);
        if (!duplicateCardId.HasValue)
        {
            return new SingletonResponse<DomainDataCard>(data: null);
        }

        return new SingletonResponse<DomainDataCard>(data: await GetById(duplicateCardId.Value));
    }

    public async Task<List<DomainDataCard>> GetAssociatedCards(int resourceId)
    {
        var resourcePvs =
            await cpvService.GetAllDbModels(x => x.ResourceId == resourceId);
        if (!resourcePvs.Any())
        {
            return [];
        }

        var types = await typeService.GetAll();
        var matchedCardIds = new HashSet<int>();

        foreach (var type in types)
        {
            if (type.MatchRules is not { AutoBindEnabled: true })
                continue;

            var matchPropertyIds = type.MatchRules.MatchProperties;
            if (matchPropertyIds == null || !matchPropertyIds.Any())
                continue;

            var resourceValuesByProperty = resourcePvs
                .Where(pv => matchPropertyIds.Contains(pv.PropertyId) && pv.Value != null)
                .GroupBy(pv => pv.PropertyId)
                .ToDictionary(g => g.Key, g => g.Select(pv => pv.Value!).ToHashSet());

            if (!resourceValuesByProperty.Any())
                continue;

            var allCardPvs = await pvOrm.GetAll(x =>
                matchPropertyIds.Contains(x.PropertyId) && x.Value != null);

            var cardPropertyValues = allCardPvs
                .GroupBy(pv => pv.CardId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var (cardId, cardPvs) in cardPropertyValues)
            {
                var matched = type.MatchRules.MatchMode == DataCardMatchMode.Any
                    ? cardPvs.Any(cpv =>
                        resourceValuesByProperty.TryGetValue(cpv.PropertyId, out var values) &&
                        values.Contains(cpv.Value!))
                    : matchPropertyIds.All(pid =>
                    {
                        var cardVal = cardPvs.FirstOrDefault(x => x.PropertyId == pid)?.Value;
                        return cardVal != null &&
                               resourceValuesByProperty.TryGetValue(pid, out var values) &&
                               values.Contains(cardVal);
                    });

                if (matched)
                {
                    matchedCardIds.Add(cardId);
                }
            }
        }

        if (!matchedCardIds.Any())
        {
            return [];
        }

        var cards = (await orm.GetAll(x => matchedCardIds.Contains(x.Id)))
            .Select(d => d.ToDomainModel())
            .ToList();
        await PopulatePropertyValues(cards);
        await ApplyNameTemplates(cards);
        return cards;
    }

    public async Task<List<int>> GetAssociatedResourceIds(int cardId)
    {
        var card = await orm.GetByKey(cardId);
        if (card == null) return [];

        var type = await typeService.GetById(card.TypeId);
        if (type?.MatchRules is not { AutoBindEnabled: true })
            return [];

        var matchPropertyIds = type.MatchRules.MatchProperties;
        if (matchPropertyIds == null || !matchPropertyIds.Any())
            return [];

        var cardPvs = await pvOrm.GetAll(x =>
            x.CardId == cardId && matchPropertyIds.Contains(x.PropertyId) && x.Value != null);

        if (!cardPvs.Any())
            return [];

        var allResourcePvs = await cpvService.GetAllDbModels(x =>
            matchPropertyIds.Contains(x.PropertyId) && x.Value != null);

        var resourcePropertyValues = allResourcePvs
            .GroupBy(pv => pv.ResourceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var matchedResourceIds = new List<int>();

        foreach (var (resourceId, resPvs) in resourcePropertyValues)
        {
            var matched = type.MatchRules.MatchMode == DataCardMatchMode.Any
                ? cardPvs.Any(cpv =>
                    resPvs.Any(rpv => rpv.PropertyId == cpv.PropertyId && rpv.Value == cpv.Value))
                : matchPropertyIds.All(pid =>
                {
                    var cardVal = cardPvs.FirstOrDefault(x => x.PropertyId == pid)?.Value;
                    return cardVal != null &&
                           resPvs.Any(rpv => rpv.PropertyId == pid && rpv.Value == cardVal);
                });

            if (matched)
            {
                matchedResourceIds.Add(resourceId);
            }
        }

        return matchedResourceIds;
    }

    public async Task<BaseResponse> DeleteByTypeId(int typeId)
    {
        var cards = await orm.GetAll(x => x.TypeId == typeId);
        if (cards.Any())
        {
            var cardIds = cards.Select(c => c.Id).ToHashSet();
            await pvOrm.RemoveAll(x => cardIds.Contains(x.CardId));
            await orm.RemoveAll(x => x.TypeId == typeId);
        }

        return BaseResponseBuilder.Ok;
    }

    public async Task<SingletonResponse<int>> CreateInitialData(int typeId, bool onlyFromResources,
        List<int>? allowNullPropertyIds)
    {
        var candidates =
            await ResolveInitialDataCandidates(typeId, onlyFromResources, allowNullPropertyIds);
        if (candidates == null || !candidates.Combinations.Any())
            return new SingletonResponse<int>(data: 0);

        var combosToCreate = candidates.Combinations
            .Where(combo => !candidates.ExistingSignatures.Contains(
                BuildComboSignature(candidates.IdentityPropertyIds, combo)))
            .ToList();

        if (combosToCreate.Count == 0)
            return new SingletonResponse<int>(data: 0);

        var now = DateTime.Now;

        var cardDbs = combosToCreate
            .Select(_ => new DataCardDbModel { TypeId = typeId, CreatedAt = now, UpdatedAt = now })
            .ToList();
        cardDbs = (await orm.AddRange(cardDbs)).Data;

        var pvDbModels = new List<DataCardPropertyValueDbModel>();
        for (var i = 0; i < combosToCreate.Count; i++)
        {
            var combo = combosToCreate[i];
            var cardId = cardDbs[i].Id;
            foreach (var pid in candidates.IdentityPropertyIds)
            {
                if (combo[pid] != null)
                {
                    pvDbModels.Add(new DataCardPropertyValueDbModel
                    {
                        CardId = cardId,
                        PropertyId = pid,
                        Value = combo[pid],
                        Scope = 0
                    });
                }
            }
        }

        if (pvDbModels.Count > 0)
            await pvOrm.AddRange(pvDbModels);

        return new SingletonResponse<int>(data: combosToCreate.Count);
    }

    public async Task<SingletonResponse<DataCardInitialDataPreview>> PreviewInitialData(int typeId,
        bool onlyFromResources, List<int>? allowNullPropertyIds)
    {
        var preview = new DataCardInitialDataPreview();
        var candidates =
            await ResolveInitialDataCandidates(typeId, onlyFromResources, allowNullPropertyIds);
        if (candidates == null)
            return new SingletonResponse<DataCardInitialDataPreview>(preview);

        foreach (var combo in candidates.Combinations)
        {
            var sig = BuildComboSignature(candidates.IdentityPropertyIds, combo);
            if (candidates.ExistingSignatures.Contains(sig))
                preview.AlreadyExists++;
            else
                preview.ToCreate++;
        }

        return new SingletonResponse<DataCardInitialDataPreview>(preview);
    }

    private sealed record InitialDataCandidates(
        List<int> IdentityPropertyIds,
        List<Dictionary<int, string?>> Combinations,
        HashSet<string> ExistingSignatures);

    private async Task<InitialDataCandidates?> ResolveInitialDataCandidates(int typeId,
        bool onlyFromResources, List<int>? allowNullPropertyIds)
    {
        var type = await typeService.GetById(typeId);
        if (type == null) return null;

        var identityPropertyIds = GetEffectiveIdentityPropertyIds(type);
        if (identityPropertyIds == null || !identityPropertyIds.Any()) return null;

        var allowNullSet = new HashSet<int>(allowNullPropertyIds ?? []);

        // Load identity properties so we can branch on reference vs non-reference and
        // consult the option pool for reference types.
        var propertiesById = (await customPropertyService.GetByKeys(identityPropertyIds))
            .ToDictionary(p => p.Id, p => p);

        var valuesPerProperty = new Dictionary<int, List<string?>>();
        foreach (var pid in identityPropertyIds)
        {
            propertiesById.TryGetValue(pid, out var property);

            var valueSet = property != null && PropertySystem.Property.IsReferenceValueType(property.Type)
                ? await GetReferencePropertyKeys(property, onlyFromResources)
                : await GetResourceDbValueKeys(pid);

            if (allowNullSet.Contains(pid))
                valueSet.Add(null);

            if (!valueSet.Any())
            {
                if (!allowNullSet.Contains(pid))
                    return new InitialDataCandidates(identityPropertyIds, [], []);
                valueSet = [null];
            }

            valuesPerProperty[pid] = valueSet.ToList();
        }

        var combinations = CartesianProduct(identityPropertyIds, valuesPerProperty);
        combinations = combinations.Where(combo =>
            identityPropertyIds.All(pid =>
                allowNullSet.Contains(pid) || combo[pid] != null)).ToList();

        var existingCards = await orm.GetAll(x => x.TypeId == typeId);
        var existingCardIds = existingCards.Select(c => c.Id).ToHashSet();
        var existingCardPvs = existingCardIds.Any()
            ? await pvOrm.GetAll(x => existingCardIds.Contains(x.CardId))
            : new List<DataCardPropertyValueDbModel>();

        var existingSignatures = new HashSet<string>();
        foreach (var cardId in existingCardIds)
        {
            var cardPvs = existingCardPvs.Where(pv => pv.CardId == cardId).ToList();
            existingSignatures.Add(BuildIdentitySignature(identityPropertyIds, cardPvs));
        }

        return new InitialDataCandidates(identityPropertyIds, combinations, existingSignatures);
    }

    private static string BuildComboSignature(List<int> propertyIds,
        Dictionary<int, string?> combo)
    {
        return string.Join("|",
            propertyIds.Select(pid => combo[pid] ?? "<null>"));
    }

    private async Task<HashSet<string?>> GetResourceDbValueKeys(int propertyId)
    {
        var pvs = await cpvService.GetAllDbModels(x =>
            x.PropertyId == propertyId && x.Value != null);
        return pvs
            .Select(pv => pv.Value)
            .Where(v => v != null)
            .Cast<string?>()
            .ToHashSet();
    }

    /// <summary>
    /// Keys for a reference-type property. Each option UUID (leaf UUID for Multilevel) becomes
    /// one key; multi-select resource values are expanded to individual UUIDs (no intra-property
    /// combinations). The stored <c>DataCardPropertyValue.Value</c> is the raw UUID string,
    /// which — for commaless UUIDs — is the same text as a single-element ListString
    /// serialization and round-trips correctly for both SingleChoice (String) and the multi-select
    /// types (ListString).
    /// <list type="bullet">
    /// <item><c>onlyFromResources=false</c>: all option UUIDs from <c>property.Options</c>.</item>
    /// <item><c>onlyFromResources=true</c>: UUIDs observed in resources ∩ current options
    ///   (expired UUIDs silently dropped).</item>
    /// </list>
    /// </summary>
    private async Task<HashSet<string?>> GetReferencePropertyKeys(
        Bakabase.Abstractions.Models.Domain.CustomProperty property, bool onlyFromResources)
    {
        var optionUuids = GetOptionUuids(property);

        IEnumerable<string> uuids;
        if (!onlyFromResources)
        {
            uuids = optionUuids;
        }
        else
        {
            var pvs = await cpvService.GetAllDbModels(x =>
                x.PropertyId == property.Id && x.Value != null);
            uuids = pvs
                .SelectMany(pv => ExpandResourceDbValueToUuids(property.Type, pv.Value!))
                .Where(optionUuids.Contains);
        }

        return uuids.Distinct().Select(u => (string?) u).ToHashSet();
    }

    private static HashSet<string> GetOptionUuids(
        Bakabase.Abstractions.Models.Domain.CustomProperty property)
    {
        var result = new HashSet<string>();
        switch (property.Type)
        {
            case PropertyType.SingleChoice:
                if (property.Options is SingleChoicePropertyOptions sco && sco.Choices != null)
                {
                    foreach (var c in sco.Choices)
                        if (!string.IsNullOrEmpty(c.Value)) result.Add(c.Value);
                }
                break;

            case PropertyType.MultipleChoice:
                if (property.Options is MultipleChoicePropertyOptions mco && mco.Choices != null)
                {
                    foreach (var c in mco.Choices)
                        if (!string.IsNullOrEmpty(c.Value)) result.Add(c.Value);
                }
                break;

            case PropertyType.Tags:
                if (property.Options is TagsPropertyOptions tpo && tpo.Tags != null)
                {
                    foreach (var t in tpo.Tags)
                        if (!string.IsNullOrEmpty(t.Value)) result.Add(t.Value);
                }
                break;

            case PropertyType.Multilevel:
                if (property.Options is MultilevelPropertyOptions mpo && mpo.Data != null)
                {
                    foreach (var uuid in EnumerateMultilevelLeafUuids(mpo.Data))
                        if (!string.IsNullOrEmpty(uuid)) result.Add(uuid);
                }
                break;
        }
        return result;
    }

    private static IEnumerable<string> ExpandResourceDbValueToUuids(PropertyType type, string dbValue)
    {
        switch (type)
        {
            case PropertyType.SingleChoice:
                if (!string.IsNullOrEmpty(dbValue)) yield return dbValue;
                break;
            case PropertyType.MultipleChoice:
            case PropertyType.Tags:
            case PropertyType.Multilevel:
                var list = dbValue.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString);
                if (list == null) yield break;
                foreach (var uuid in list)
                {
                    if (!string.IsNullOrEmpty(uuid)) yield return uuid;
                }
                break;
        }
    }

    private static IEnumerable<string> EnumerateMultilevelLeafUuids(List<MultilevelDataOptions> nodes)
    {
        foreach (var node in nodes)
        {
            var isLeaf = node.Children == null || node.Children.Count == 0;
            if (isLeaf)
            {
                if (!string.IsNullOrEmpty(node.Value)) yield return node.Value;
            }
            else
            {
                foreach (var childUuid in EnumerateMultilevelLeafUuids(node.Children!))
                    yield return childUuid;
            }
        }
    }

    private static List<int>? GetEffectiveIdentityPropertyIds(DataCardType type)
    {
        if (type.IdentityPropertyIds?.Any() == true)
            return type.IdentityPropertyIds;
        return type.PropertyIds;
    }

    private async Task<int?> FindDuplicateCardId(int typeId, List<int> identityPropertyIds,
        List<DataCardPropertyValueInputModel>? inputPropertyValues, int? excludeCardId)
    {
        var targetSig = string.Join("|",
            identityPropertyIds.Select(pid =>
                inputPropertyValues?.FirstOrDefault(pv => pv.PropertyId == pid)?.Value ?? "<null>"));

        var existingCards = await orm.GetAll(x => x.TypeId == typeId);
        if (!existingCards.Any()) return null;

        var existingCardIds = existingCards
            .Where(c => !excludeCardId.HasValue || c.Id != excludeCardId.Value)
            .Select(c => c.Id)
            .ToHashSet();
        if (!existingCardIds.Any()) return null;

        var existingPvs = await pvOrm.GetAll(x => existingCardIds.Contains(x.CardId));

        foreach (var cardId in existingCardIds)
        {
            var cardPvs = existingPvs.Where(pv => pv.CardId == cardId).ToList();
            var sig = BuildIdentitySignature(identityPropertyIds, cardPvs);
            if (sig == targetSig)
            {
                return cardId;
            }
        }

        return null;
    }

    private static string BuildIdentitySignature(List<int> propertyIds,
        List<DataCardPropertyValueDbModel> cardPvs)
    {
        return string.Join("|",
            propertyIds.Select(pid =>
                cardPvs.FirstOrDefault(pv => pv.PropertyId == pid)?.Value ?? "<null>"));
    }

    private static List<Dictionary<int, string?>> CartesianProduct(
        List<int> propertyIds, Dictionary<int, List<string?>> valuesPerProperty)
    {
        var result = new List<Dictionary<int, string?>> { new() };

        foreach (var pid in propertyIds)
        {
            var values = valuesPerProperty[pid];
            var newResult = new List<Dictionary<int, string?>>();

            foreach (var existing in result)
            {
                foreach (var value in values)
                {
                    var combo = new Dictionary<int, string?>(existing) { [pid] = value };
                    newResult.Add(combo);
                }
            }

            result = newResult;
        }

        return result;
    }

    private async Task PopulatePropertyValues(List<DomainDataCard> cards)
    {
        if (!cards.Any()) return;
        var cardIds = cards.Select(c => c.Id).ToHashSet();
        var allPvs = await pvOrm.GetAll(x => cardIds.Contains(x.CardId));
        var pvsByCard = allPvs.GroupBy(pv => pv.CardId)
            .ToDictionary(g => g.Key, g => g.Select(pv => pv.ToDomainModel()).ToList());

        foreach (var card in cards)
        {
            card.PropertyValues = pvsByCard.GetValueOrDefault(card.Id);
        }
    }

    private async Task ApplyNameTemplates(List<DomainDataCard> cards)
    {
        if (!cards.Any()) return;

        var typeIds = cards.Select(c => c.TypeId).Distinct().ToList();
        var types = (await typeService.GetAll(t => typeIds.Contains(t.Id)))
            .ToDictionary(t => t.Id, t => t);

        var needsTemplate = cards
            .Where(c => types.TryGetValue(c.TypeId, out var t) && !string.IsNullOrWhiteSpace(t.NameTemplate))
            .ToList();
        if (!needsTemplate.Any()) return;

        var referencedPropertyIds = needsTemplate
            .SelectMany(c => types[c.TypeId].PropertyIds ?? [])
            .Distinct()
            .ToList();
        if (!referencedPropertyIds.Any()) return;

        var properties = (await customPropertyService.GetByKeys(referencedPropertyIds))
            .ToDictionary(p => p.Id, p => p);

        foreach (var card in needsTemplate)
        {
            var type = types[card.TypeId];
            var template = type.NameTemplate!;
            var valueByName = new Dictionary<string, string>(StringComparer.Ordinal);
            if (card.PropertyValues != null)
            {
                foreach (var pv in card.PropertyValues)
                {
                    if (!properties.TryGetValue(pv.PropertyId, out var cp) || string.IsNullOrEmpty(cp.Name))
                        continue;

                    var dbValue = pv.Value?.DeserializeAsStandardValue(cp.Type.GetDbValueType());
                    var bizValue = PropertySystem.Property.ToBizValue(cp.ToProperty(), dbValue);
                    valueByName[cp.Name] =
                        bizValue?.SerializeAsStandardValue(cp.Type.GetBizValueType()) ?? string.Empty;
                }
            }

            var rendered = NameTemplateTokenRegex.Replace(template, m =>
            {
                var key = m.Groups[1].Value;
                return valueByName.TryGetValue(key, out var v) ? v : string.Empty;
            }).Trim();

            if (!string.IsNullOrEmpty(rendered))
            {
                card.Name = rendered;
            }
        }
    }
}
