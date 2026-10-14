using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Search;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.Search.Extensions;
using Bakabase.Modules.Search.Models.Db;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>Read-only matching runs with the source library's definitions, never the viewer's integer IDs.</summary>
public sealed class ResourceProfileSourceEvaluator(IResourceService resources, IPropertyService properties,
    IResourceLegacySearchService matcher) : IResourceProfileSourceEvaluator
{
    public const int MaximumResources = 256;
    public const int MaximumProfiles = 128;
    public const int MaximumSearchBytes = 1024 * 1024;

    public async Task<ResourceProfileEvaluationResult> EvaluateAsync(ResourceProfileEvaluationInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.ResourceIds == null || input.Profiles == null || input.ResourceIds.Length > MaximumResources ||
            input.Profiles.Length > MaximumProfiles || input.ResourceIds.Any(id => id <= 0) ||
            input.Profiles.Any(p => p == null || p.Id <= 0) ||
            input.Profiles.Select(p => p.Id).Distinct().Count() != input.Profiles.Length ||
            input.Profiles.Sum(p => (long)Encoding.UTF8.GetByteCount(p.SearchJson ?? string.Empty)) > MaximumSearchBytes)
            throw new ArgumentException("The profile match request exceeds its limits or contains invalid identities.");

        cancellationToken.ThrowIfCancellationRequested();
        var existing = await resources.GetByKeys(input.ResourceIds.Distinct().ToArray(), ResourceAdditionalItem.None);
        var matches = existing.ToDictionary(r => r.Id, _ => new List<int>());
        if (matches.Count == 0 || input.Profiles.Length == 0)
            return Result(matches);

        var propertyMap = await GetPropertyMap();
        var searches = new List<ResourceSearch>();
        foreach (var rule in input.Profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            searches.Add(ParseSearch(rule.SearchJson, propertyMap));
        }
        // A card/page only asks about its visible resource IDs. Avoid materializing a library-wide
        // match set per profile; all rules share one bounded property-value snapshot instead.
        try
        {
            var results = await matcher.SearchManyAsync(existing, searches);
            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i < input.Profiles.Length; i++)
                foreach (var id in matches.Keys)
                    if (results[i]?.Contains(id) != false) matches[id].Add(input.Profiles[i].Id);
        }
        catch (RegexMatchTimeoutException e)
        { throw new ArgumentException("A source filter's regular expression took too long to evaluate.", e); }
        return Result(matches);
    }

    public async Task<ResourceSearch> ParseSearchAsync(string? searchJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ParseSearch(searchJson, await GetPropertyMap());
    }

    private async Task<Dictionary<PropertyPool, Dictionary<int, Property>>> GetPropertyMap() =>
        (await properties.GetProperties(PropertyPool.All)).GroupBy(p => p.Pool)
            .ToDictionary(g => g.Key, g => g.ToDictionary(p => p.Id));

    private static ResourceSearch ParseSearch(string? searchJson,
        Dictionary<PropertyPool, Dictionary<int, Property>> propertyMap)
    {
        if (Encoding.UTF8.GetByteCount(searchJson ?? string.Empty) > MaximumSearchBytes)
            throw new ArgumentException("The source filter exceeds its size limit.");
        try
        {
            var search = string.IsNullOrWhiteSpace(searchJson) ? new ResourceSearchDbModel() :
                JsonConvert.DeserializeObject<ResourceSearchDbModel>(searchJson,
                    new JsonSerializerSettings { MaxDepth = 32, MissingMemberHandling = MissingMemberHandling.Error }) ?? throw new ArgumentException("The source filter is empty.");
            ValidateGroup(search.Group, propertyMap, 0);
            // The ordinary converter omits deleted properties. Reject them first so a
            // stale narrow rule cannot silently become a catch-all rule.
            var domain = search.ToDomainModel(propertyMap);
            // Match the ordinary resource-search keyword behavior: filename plus searchable custom
            // properties, combined with explicit filters using AND. Use the source's vocabulary.
            if (!string.IsNullOrEmpty(search.Keyword))
            {
                var keywordFilters = propertyMap.GetValueOrDefault(PropertyPool.Custom)?.Values
                    .Select(p => PropertySystem.Property.TryGetSearchHandler(p.Type)
                        ?.BuildSearchFilterByKeyword(p, search.Keyword))
                    .OfType<ResourceSearchFilter>().ToList() ?? [];
                var filename = propertyMap[PropertyPool.Internal][(int)InternalProperty.Filename];
                keywordFilters.Add(PropertySystem.Property.GetSearchHandler(filename.Type)
                    .BuildSearchFilterByKeyword(filename, search.Keyword)!);
                var keywordGroup = new ResourceSearchFilterGroup
                    { Combinator = SearchCombinator.Or, Filters = keywordFilters };
                domain.Group = domain.Group == null ? keywordGroup : new ResourceSearchFilterGroup
                    { Combinator = SearchCombinator.And, Groups = [domain.Group, keywordGroup] };
            }
            return domain;
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidCastException or RegexParseException)
        { throw new ArgumentException("A source filter or its value is invalid.", e); }
    }

    private static ResourceProfileEvaluationResult Result(Dictionary<int, List<int>> matches) => new()
    {
        Matches = matches.Select(pair => new ResourceProfileResourceMatch
        { ResourceId = pair.Key, ProfileIds = pair.Value.ToArray() }).ToArray()
    };

    private static void ValidateGroup(ResourceSearchFilterGroupDbModel? group,
        Dictionary<PropertyPool, Dictionary<int, Property>> propertyMap, int depth)
    {
        if (group == null || group.Disabled) return;
        if (depth > 24 || !Enum.IsDefined(group.Combinator))
            throw new ArgumentException("The source filter group is invalid.");
        foreach (var filter in group.Filters ?? [])
        {
            if (filter == null) throw new ArgumentException("A source filter is missing.");
            if (filter.Disabled) continue;
            if (!filter.IsValid() || !propertyMap.TryGetValue(filter.PropertyPool!.Value, out var pool) ||
                !pool.TryGetValue(filter.PropertyId!.Value, out var property) ||
                PropertySystem.Property.TryGetSearchHandler(property.Type)?.SearchOperations
                    .ContainsKey(filter.Operation!.Value) != true)
                throw new ArgumentException("A source filter references a missing property or unsupported operation. Edit this profile using the source device's current fields.");
            if (filter.Value != null)
            {
                var handler = PropertySystem.Property.TryGetSearchHandler(property.Type)!;
                var valueType = handler.SearchOperations[filter.Operation!.Value]?.AsType ?? property.Type;
                _ = filter.Value.DeserializeAsStandardValue(valueType.GetDbValueType(), throwOnError: true);
                if (filter.Operation is SearchOperation.Matches or SearchOperation.NotMatches)
                    _ = new Regex(filter.Value, RegexOptions.None, TimeSpan.FromSeconds(1));
            }
        }
        foreach (var child in group.Groups ?? [])
        {
            if (child == null) throw new ArgumentException("A source filter group is missing.");
            ValidateGroup(child, propertyMap, depth + 1);
        }
    }
}
