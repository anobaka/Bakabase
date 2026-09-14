using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Platform;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition;

/// <summary>
/// Joins the resource search, stored links, platform identities and actual workflow definitions.
/// The number of service/database reads is independent of the number of rows. No connector is
/// constructed: knowing that a build implements a platform is different from asking an account
/// whether it owns a particular work, and browsing must never start that work.
/// </summary>
public class AcquisitionCandidateService(
    IResourceService resources,
    IPropertyService properties,
    IReservedPropertyValueService names,
    IResourceSourceLinkService sourceLinks,
    IAcquisitionLeadService leads,
    IAcquisitionService acquisitions,
    IPlatformConnectorRegistry platforms,
    IBOptions<AcquisitionOptions> options,
    BakabaseDbContext db)
{
    private static readonly AcquisitionStatus[] Live =
        [AcquisitionStatus.Pending, AcquisitionStatus.Running, AcquisitionStatus.Waiting];

    public async Task<AcquisitionCandidatePageViewModel> SearchAsync(string? keyword = null,
        int page = 1, int pageSize = 24, string filter = "all", CancellationToken ct = default)
    {
        if (filter is not ("all" or "withSources" or "withoutSources" or "unsupported"))
        {
            throw new ArgumentException("Unknown acquisition candidate filter.", nameof(filter));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var recipes = await acquisitions.GetRecipesAsync(ct);
        var ids = await SearchResourceIds(keyword);

        // Filtering by routes must happen before pagination: filtering a page after loading it
        // would hide later matches and give the user an incorrect total. The unfiltered overview
        // only needs route details for its visible page, even when the library is very large.
        var lookupIds = filter == "all"
            ? ids.OrderByDescending(id => id)
                .Skip((int) Math.Min((long) (page - 1) * pageSize, ids.Length)).Take(pageSize).ToArray()
            : ids;
        var routes = await LoadRoutesAsync(lookupIds, recipes, ct);

        var matched = ids.Where(id => filter switch
        {
            "withSources" => routes[id].Count > 0,
            "withoutSources" => routes[id].Count == 0,
            "unsupported" => routes[id].Count > 0 && routes[id].All(l => l.Capability != "supported"),
            _ => true
        }).OrderByDescending(id => id).ToArray();
        var skip = Math.Min((long) (page - 1) * pageSize, matched.Length);
        var pageIds = matched.Skip((int) skip).Take(pageSize).ToArray();

        return await BuildPageAsync(pageIds, routes, matched.Length, page, pageSize, recipes, ct);
    }

    /// <summary>Reads one resource's routes directly, without a global resource search or platform probe.</summary>
    public async Task<AcquisitionCandidatePageViewModel> GetAsync(int resourceId, CancellationToken ct = default)
    {
        var recipes = await acquisitions.GetRecipesAsync(ct);
        var resource = await resources.Get(resourceId);
        if (resource == null || resource.HasLocalPath)
        {
            return new AcquisitionCandidatePageViewModel([], 0, 1, 1, recipes);
        }

        var ids = new[] {resourceId};
        var routes = await LoadRoutesAsync(ids, recipes, ct);
        return await BuildPageAsync(ids, routes, 1, 1, 1, recipes, ct);
    }

    private async Task<Dictionary<int, List<AcquisitionCandidateLeadViewModel>>> LoadRoutesAsync(
        int[] ids, List<AcquisitionRecipeSummary> recipes, CancellationToken ct)
    {
        var stored = await leads.GetByResourceIds(ids);
        var identities = await sourceLinks.GetByResourceIdsGrouped(ids);
        var routes = new Dictionary<int, List<AcquisitionCandidateLeadViewModel>>();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var known = (identities.GetValueOrDefault(id) ?? [])
                .Where(l => l.Source.IsPlatformHolding())
                .Select(l => Describe(new AcquisitionLead
                {
                    ResourceId = id,
                    Kind = AcquisitionLeadKind.PlatformHolding,
                    Value = $"{l.Source}:{l.SourceKey}",
                    IsDerived = true,
                    SourceName = l.Source.ToString()
                }, recipes, l.Source))
                .Concat((stored.GetValueOrDefault(id) ?? []).Select(l => Describe(l, recipes)))
                .ToList();
            routes[id] = known;
        }

        return routes;
    }

    private async Task<AcquisitionCandidatePageViewModel> BuildPageAsync(int[] pageIds,
        Dictionary<int, List<AcquisitionCandidateLeadViewModel>> routes, int totalCount, int page,
        int pageSize, List<AcquisitionRecipeSummary> recipes, CancellationToken ct)
    {
        if (pageIds.Length == 0)
        {
            return new AcquisitionCandidatePageViewModel([], totalCount, page, pageSize, recipes);
        }

        var titles = (await names.GetAll(v => pageIds.Contains(v.ResourceId)))
            .Where(v => !string.IsNullOrWhiteSpace(v.Name))
            .GroupBy(v => v.ResourceId)
            .ToDictionary(g => g.Key, g => g.First().Name!);
        var active = (await db.Set<AcquisitionTaskDbModel>().AsNoTracking()
                .Where(t => pageIds.Contains(t.ResourceId) && Live.Contains(t.Status))
                .OrderByDescending(t => t.Id).ToListAsync(ct))
            .GroupBy(t => t.ResourceId)
            .ToDictionary(g => g.Key, g => g.First());

        var items = pageIds.Select(id =>
        {
            var task = active.GetValueOrDefault(id);
            return new AcquisitionCandidateViewModel(id, titles.GetValueOrDefault(id) ?? $"#{id}",
                routes[id], task?.Id, task?.Status);
        }).ToList();
        return new AcquisitionCandidatePageViewModel(items, totalCount, page, pageSize, recipes);
    }

    private async Task<int[]> SearchResourceIds(string? keyword)
    {
        var allProperties = await properties.GetProperties(PropertyPool.Internal | PropertyPool.Reserved);
        var group = new ResourceSearchFilterGroup
        {
            Combinator = SearchCombinator.And,
            Filters =
            [
                new ResourceSearchFilter
                {
                    PropertyPool = PropertyPool.Internal,
                    PropertyId = (int) InternalProperty.HasLocalPath,
                    Property = allProperties.First(p => p.Pool == PropertyPool.Internal &&
                                                        p.Id == (int) InternalProperty.HasLocalPath),
                    Operation = SearchOperation.Equals,
                    DbValue = false
                }
            ]
        };

        // A pathless resource's title is the reserved Name, not Filename. Use the shared search
        // evaluator so the index and its full-scan fallback agree about the same title query.
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            group.Filters.Add(new ResourceSearchFilter
            {
                PropertyPool = PropertyPool.Reserved,
                PropertyId = (int) ReservedProperty.Name,
                Property = allProperties.First(p => p.Pool == PropertyPool.Reserved &&
                                                    p.Id == (int) ReservedProperty.Name),
                Operation = SearchOperation.Contains,
                DbValue = keyword.Trim()
            });
        }

        return await resources.GetAllIds(new ResourceSearch {Group = group});
    }

    private AcquisitionCandidateLeadViewModel Describe(AcquisitionLead lead,
        List<AcquisitionRecipeSummary> recipes, ResourceSource? platform = null)
    {
        var defaultName = BuiltinAcquisitionRecipes.DefaultRecipeNameFor(lead.Kind, options.Value);
        var defaultRecipe = recipes.Where(r => r.Name == defaultName)
            .OrderBy(r => r.DefinitionId).FirstOrDefault();
        var applicable = recipes.Where(r => r.ApplicableLeadKinds.Contains(lead.Kind))
            .Select(r => r.DefinitionId).ToList();
        var unsupportedPlatform = platform.HasValue && !platforms.Sources.Contains(platform.Value);
        var capability = unsupportedPlatform ? "unsupportedPlatform"
            : applicable.Count == 0 ? "noApplicableRecipe" : "supported";
        if (unsupportedPlatform) applicable.Clear();

        return new AcquisitionCandidateLeadViewModel(lead.Id, lead.Kind, lead.Value, lead.SourceName,
            lead.IsDerived, lead.Note, "unknown", capability, "workflow",
            defaultName, defaultRecipe?.DefinitionId, applicable);
    }

}
