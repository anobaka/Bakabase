using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Search.Models.Db;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>Shared conditions and independent rule writes, with database guards against stale editors.</summary>
public sealed class ResourceScopeService<TDbContext>(TDbContext db,
    FullMemoryCacheResourceService<TDbContext, ResourceScopeDbModel, int> scopes,
    FullMemoryCacheResourceService<TDbContext, ResourceProfileDbModel, int> profiles,
    IServiceProvider services) : IResourceScopeService where TDbContext : DbContext
{
    private IResourceProfileService Profiles => services.GetRequiredService<IResourceProfileService>();
    private IResourceProfileIndexService Index => services.GetRequiredService<IResourceProfileIndexService>();

    public Task<List<ResourceScopeDbModel>> GetAll() => scopes.GetAll();

    private async Task Validate(string name, string? json, string? source, string? epoch)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 128)
            throw new ArgumentException("Use a scope name of 1–128 characters.");
        ResourceProfileScope.Validate(source, epoch, null, null, null, null, null);
        if (Encoding.UTF8.GetByteCount(json ?? "") > 1024 * 1024)
            throw new ArgumentException("The resource condition is too large.");
        try
        {
            _ = JsonConvert.DeserializeObject<ResourceSearchDbModel>(json ?? "{}",
                new JsonSerializerSettings { MaxDepth = 32, MissingMemberHandling = MissingMemberHandling.Error })
                ?? throw new ArgumentException("A resource condition is required.");
        }
        catch (JsonException e) { throw new ArgumentException("The resource condition is invalid.", e); }
        if (source == null)
            await services.GetRequiredService<IResourceProfileSourceEvaluator>().ParseSearchAsync(json);
    }

    public async Task<ResourceScopeDbModel> Add(string name, string? searchJson, string? sourceDeviceId, string? sourceLibraryEpoch)
    {
        await Validate(name, searchJson, sourceDeviceId, sourceLibraryEpoch);
        var now = DateTime.UtcNow;
        var scope = new ResourceScopeDbModel { Name = name.Trim(), SearchJson = searchJson,
            SourceDeviceId = sourceDeviceId, SourceLibraryEpoch = sourceLibraryEpoch, CreatedAt = now, UpdatedAt = now };
        await scopes.Add(scope);
        db.Detach(scope);
        return scope;
    }

    public async Task<ResourceScopeDbModel> Update(int id, int expectedRevision, string name, string? searchJson)
    {
        var before = await db.Set<ResourceScopeDbModel>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == id)
            ?? throw new ResourceScopeConflictException("The resource scope no longer exists. Refresh before saving.");
        await Validate(name, searchJson, before.SourceDeviceId, before.SourceLibraryEpoch);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var changed = await db.Set<ResourceScopeDbModel>().Where(s => s.Id == id && s.Revision == expectedRevision)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.Name, name.Trim()).SetProperty(s => s.SearchJson, searchJson)
                .SetProperty(s => s.Revision, s => s.Revision + 1).SetProperty(s => s.UpdatedAt, now));
        if (changed != 1) throw new ResourceScopeConflictException("This scope changed elsewhere. Reload before saving.");
        await transaction.CommitAsync();
        scopes.ClearCache();
        var affected = await profiles.GetAll(p => p.ResourceScopeId == id);
        foreach (var profile in affected) Index.InvalidateProfile(profile.Id);
        if (before.SourceDeviceId == null && before.SearchJson != searchJson && affected.Any(p => p.PlayableFileSettingsJson != null))
            await InvalidatePlayable(before.SearchJson, searchJson);
        return (await scopes.GetByKey(id))!;
    }

    public async Task Delete(int id, int expectedRevision)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var changed = await db.Set<ResourceScopeDbModel>().Where(s => s.Id == id && s.Revision == expectedRevision &&
            !db.Set<ResourceProfileDbModel>().Any(p => p.ResourceScopeId == s.Id)).ExecuteDeleteAsync();
        if (changed != 1)
            throw new ResourceScopeConflictException("This scope changed or is still used by rules. Refresh and unlink those rules before deleting it.");
        await transaction.CommitAsync();
        scopes.ClearCache();
    }

    public async Task<ResourceProfile> Bind(int profileId, int? scopeId, DateTime expectedUpdatedAt)
    {
        var before = await Profiles.Get(profileId) ?? throw new ResourceScopeConflictException("The rule no longer exists.");
        var scope = scopeId == null ? null : await scopes.GetByKey(scopeId.Value)
            ?? throw new ResourceScopeConflictException("The resource scope no longer exists.");
        if (scope != null && (scope.SourceDeviceId != before.SourceDeviceId || scope.SourceLibraryEpoch != before.SourceLibraryEpoch))
            throw new ArgumentException("A reusable condition must belong to the same source device and library as this rule.");
        var effectiveSearch = scope != null ? scope.SearchJson : before.SearchJson;
        var previousScope = scopeId == null && before.ResourceScopeId != null
            ? await scopes.GetByKey(before.ResourceScopeId.Value) : null;
        if (before.ResourceScopeId != null && scopeId == null && (previousScope == null || previousScope.SearchJson != before.SearchJson))
            throw new ResourceScopeConflictException("This scope changed. Reload before unlinking it.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Recheck scope membership under the same transaction; a stale scope selection must never broaden a rule.
        if (scope != null && !await db.Set<ResourceScopeDbModel>().AnyAsync(s => s.Id == scope.Id &&
                s.Revision == scope.Revision && s.SourceDeviceId == before.SourceDeviceId && s.SourceLibraryEpoch == before.SourceLibraryEpoch))
            throw new ResourceScopeConflictException("This scope changed. Reload before selecting it.");
        if (previousScope != null && !await db.Set<ResourceScopeDbModel>().AnyAsync(s => s.Id == previousScope.Id && s.Revision == previousScope.Revision))
            throw new ResourceScopeConflictException("This scope changed. Reload before unlinking it.");
        var now = DateTime.UtcNow;
        var changed = await db.Set<ResourceProfileDbModel>().Where(p => p.Id == profileId && p.UpdatedAt == expectedUpdatedAt)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.ResourceScopeId, scopeId)
                .SetProperty(p => p.SearchJson, effectiveSearch).SetProperty(p => p.UpdatedAt, now));
        if (changed != 1) throw new ResourceScopeConflictException("This rule changed elsewhere. Reload before saving.");
        await transaction.CommitAsync();
        profiles.ClearCache();
        Index.InvalidateProfile(profileId);
        if (before.SourceDeviceId == null && before.PlayableFileOptions != null && before.SearchJson != effectiveSearch)
            await InvalidatePlayable(before.SearchJson, effectiveSearch);
        return (await Profiles.Get(profileId))!;
    }

    public async Task<ResourceProfile> UpdateRule(int profileId, ResourceProfileRuleKind kind, string? valueJson, DateTime expectedUpdatedAt)
    {
        var before = await Profiles.Get(profileId) ?? throw new ResourceScopeConflictException("The rule no longer exists.");
        if (before.SourceDeviceId != null && kind is not (ResourceProfileRuleKind.Players or ResourceProfileRuleKind.OpeningMode or ResourceProfileRuleKind.Enhancers or ResourceProfileRuleKind.PlayableFiles))
            throw new ArgumentException("Rules for another device cannot change name templates or property display settings.");
        if (Encoding.UTF8.GetByteCount(valueJson ?? "") > 1024 * 1024) throw new ArgumentException("The rule is too large.");
        string? Json<T>()
        {
            var value = valueJson == null ? default : JsonConvert.DeserializeObject<T>(valueJson,
                new JsonSerializerSettings { MaxDepth = 32, MissingMemberHandling = MissingMemberHandling.Error });
            return value == null ? null : JsonConvert.SerializeObject(value);
        }
        string? value;
        try
        {
            value = kind switch
            {
                ResourceProfileRuleKind.NameTemplate or ResourceProfileRuleKind.OpeningMode => valueJson == null ? null : JsonConvert.DeserializeObject<string>(valueJson),
                ResourceProfileRuleKind.Enhancers => Json<ResourceProfileEnhancerOptions>(),
                ResourceProfileRuleKind.PlayableFiles => Json<ResourceProfilePlayableFileOptions>(),
                ResourceProfileRuleKind.Players => Json<ResourceProfilePlayerOptions>(),
                ResourceProfileRuleKind.Properties => Json<ResourceProfilePropertyOptions>(),
                _ => throw new ArgumentException("Choose a valid rule type.")
            };
            if (kind == ResourceProfileRuleKind.OpeningMode && value is not (null or "browser" or "native" or "ask"))
                throw new ArgumentException("Choose browser, native, ask, or inherit.");
            if (kind == ResourceProfileRuleKind.PlayableFiles && value != null)
            {
                var options = JsonConvert.DeserializeObject<ResourceProfilePlayableFileOptions>(value)!;
                options.NormalizeExtensions();
                value = JsonConvert.SerializeObject(options);
            }
        }
        catch (JsonException e) { throw new ArgumentException("The rule settings are invalid.", e); }
        ResourceProfileScope.Validate(before.SourceDeviceId, before.SourceLibraryEpoch, before.OpeningMode,
            before.NameTemplate,
            kind == ResourceProfileRuleKind.Enhancers ? (value == null ? null : JsonConvert.DeserializeObject<ResourceProfileEnhancerOptions>(value)) : before.EnhancerOptions,
            kind == ResourceProfileRuleKind.PlayableFiles ? (value == null ? null : JsonConvert.DeserializeObject<ResourceProfilePlayableFileOptions>(value)) : before.PlayableFileOptions,
            before.PropertyOptions);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var query = db.Set<ResourceProfileDbModel>().Where(p => p.Id == profileId && p.UpdatedAt == expectedUpdatedAt);
        var now = DateTime.UtcNow;
        var changed = kind switch
        {
            ResourceProfileRuleKind.NameTemplate => await query.ExecuteUpdateAsync(set => set.SetProperty(p => p.NameTemplate, value).SetProperty(p => p.UpdatedAt, now)),
            ResourceProfileRuleKind.Enhancers => await query.ExecuteUpdateAsync(set => set.SetProperty(p => p.EnhancerSettingsJson, value).SetProperty(p => p.UpdatedAt, now)),
            ResourceProfileRuleKind.PlayableFiles => await query.ExecuteUpdateAsync(set => set.SetProperty(p => p.PlayableFileSettingsJson, value).SetProperty(p => p.UpdatedAt, now)),
            ResourceProfileRuleKind.Players => await query.ExecuteUpdateAsync(set => set.SetProperty(p => p.PlayerSettingsJson, value).SetProperty(p => p.UpdatedAt, now)),
            ResourceProfileRuleKind.Properties => await query.ExecuteUpdateAsync(set => set.SetProperty(p => p.PropertiesJson, value).SetProperty(p => p.UpdatedAt, now)),
            ResourceProfileRuleKind.OpeningMode => await query.ExecuteUpdateAsync(set => set.SetProperty(p => p.OpeningMode, value).SetProperty(p => p.UpdatedAt, now)),
            _ => throw new ArgumentException("Choose a valid rule type.")
        };
        if (changed != 1) throw new ResourceScopeConflictException("This rule changed elsewhere. Reload before saving.");
        await transaction.CommitAsync();
        profiles.ClearCache();
        Index.InvalidateProfile(profileId);
        if (kind == ResourceProfileRuleKind.PlayableFiles && before.SourceDeviceId == null)
            await InvalidatePlayable(before.SearchJson, before.SearchJson);
        return (await Profiles.Get(profileId))!;
    }

    private async Task InvalidatePlayable(string? previous, string? next)
    {
        var ids = await Profiles.GetMatchingResourceIdsBySearchJson(previous ?? "{}");
        ids.UnionWith(await Profiles.GetMatchingResourceIdsBySearchJson(next ?? "{}"));
        if (ids.Count > 0)
            await services.GetRequiredService<IResourceService>().DeleteResourceCacheByResourceIdsAndCacheType(ids, ResourceCacheType.PlayableFiles);
    }
}
