using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services;

/// <inheritdoc />
public class ResourceMatchSuggestionService(
    FullMemoryCacheResourceService<BakabaseDbContext, ResourceMatchSuggestionDbModel, int> orm,
    IResourceService resources,
    IReservedPropertyValueService reservedPropertyValues,
    IResourceSourceLinkService sourceLinks,
    IAcquisitionLeadService leads,
    ICollectionResourceMappingService collectionMappings,
    ILogger<ResourceMatchSuggestionService> logger) : IResourceMatchSuggestionService
{
    public async Task<List<ResourceMatchSuggestion>> GetPending(CancellationToken ct = default)
    {
        var rows = (await orm.GetAll(x => x.Status == ResourceMatchSuggestionStatus.Pending))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.CreatedAt)
            .ToList();

        if (rows.Count == 0) return [];

        var names = await ReadNames(rows.SelectMany(r => new[] {r.ResourceId, r.CandidateResourceId})
            .ToHashSet());

        return rows.Select(r => new ResourceMatchSuggestion
        {
            Id = r.Id,
            ResourceId = r.ResourceId,
            ResourceName = names.GetValueOrDefault(r.ResourceId),
            CandidateResourceId = r.CandidateResourceId,
            CandidateResourceName = names.GetValueOrDefault(r.CandidateResourceId),
            Score = r.Score,
            Reason = r.Reason,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList();
    }

    public async Task<int> CountPending(CancellationToken ct = default) =>
        (await orm.GetAll(x => x.Status == ResourceMatchSuggestionStatus.Pending)).Count;

    public async Task Suggest(int resourceId, IReadOnlyList<ResourceMatchCandidate> candidates,
        CancellationToken ct = default)
    {
        if (candidates.Count == 0) return;

        // Every pair ever raised about this resource, whatever came of it. A pair the user has
        // already told apart must not come back the next time the same source lists the same thing.
        var decided = (await orm.GetAll(x => x.ResourceId == resourceId))
            .Select(x => x.CandidateResourceId)
            .ToHashSet();

        var rows = new List<ResourceMatchSuggestionDbModel>();

        foreach (var candidate in candidates)
        {
            if (candidate.CandidateResourceId == resourceId) continue;
            if (!decided.Add(candidate.CandidateResourceId)) continue;

            rows.Add(new ResourceMatchSuggestionDbModel
            {
                ResourceId = resourceId,
                CandidateResourceId = candidate.CandidateResourceId,
                Score = candidate.Score,
                Reason = candidate.Reason,
                Status = ResourceMatchSuggestionStatus.Pending,
                CreatedAt = DateTime.Now
            });
        }

        if (rows.Count > 0) await orm.AddRange(rows);
    }

    public async Task Confirm(int id, CancellationToken ct = default)
    {
        var suggestion = await orm.GetByKey(id, false);

        if (suggestion is not {Status: ResourceMatchSuggestionStatus.Pending}) return;

        // Merging deletes the resource that was asked about, and deleting a resource takes every
        // suggestion naming it — this one included, along with anything else that was still asked
        // about it. There is nothing left to mark afterwards, which is the honest outcome: the
        // question stopped existing when one of its two answers did.
        await Merge(suggestion.ResourceId, suggestion.CandidateResourceId, ct);
    }

    public async Task Dismiss(int id, CancellationToken ct = default)
    {
        var suggestion = await orm.GetByKey(id, false);

        if (suggestion is not {Status: ResourceMatchSuggestionStatus.Pending}) return;

        await orm.UpdateByKey(id, x =>
        {
            x.Status = ResourceMatchSuggestionStatus.Dismissed;
            x.DecidedAt = DateTime.Now;
        });
    }

    public async Task DeleteByResourceIds(IReadOnlyCollection<int> resourceIds,
        CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return;

        var ids = resourceIds.ToHashSet();

        await orm.RemoveAll(x => ids.Contains(x.ResourceId) || ids.Contains(x.CandidateResourceId));
    }

    /// <summary>
    /// Moves everything the merged-away resource carries onto the one that stays, then deletes it.
    /// <para>
    /// Only a resource with nothing on disk can go: a resource with files has a path somebody's
    /// player, mark or media library refers to, and no similarity score is worth losing that.
    /// </para>
    /// </summary>
    private async Task Merge(int fromResourceId, int intoResourceId, CancellationToken ct)
    {
        var from = await resources.Get(fromResourceId);
        var into = await resources.Get(intoResourceId);

        if (from == null || into == null)
        {
            throw new InvalidOperationException(
                "One of the two resources is gone, so there is nothing left to merge.");
        }

        if (from.HasLocalPath)
        {
            throw new InvalidOperationException(
                $"Resource {fromResourceId} has files on disk, so it cannot be merged away.");
        }

        await MoveSourceLinks(fromResourceId, intoResourceId);
        await MoveLeads(fromResourceId, intoResourceId);
        await MoveCollectionMemberships(fromResourceId, intoResourceId, ct);

        await resources.DeleteByKeys([fromResourceId]);
    }

    private async Task MoveSourceLinks(int fromResourceId, int intoResourceId)
    {
        var moving = await sourceLinks.GetByResourceId(fromResourceId);

        if (moving.Count == 0) return;

        var existing = (await sourceLinks.GetByResourceId(intoResourceId))
            .Select(l => l.Source)
            .ToHashSet();

        // An identity the surviving resource already has on the same platform is the authority —
        // overwriting it with the newcomer's would silently repoint an established resource.
        var links = moving.Where(l => !existing.Contains(l.Source)).Select(l =>
        {
            l.ResourceId = intoResourceId;

            return l;
        }).ToList();

        if (links.Count > 0) await sourceLinks.EnsureLinks(intoResourceId, links);

        await sourceLinks.DeleteByResourceId(fromResourceId);
    }

    private async Task MoveLeads(int fromResourceId, int intoResourceId)
    {
        foreach (var lead in await leads.GetByResourceId(fromResourceId))
        {
            // The lead's value is unique across resources, so it has to leave the old resource
            // before it can join the new one.
            await leads.Delete(lead.Id);

            try
            {
                await leads.Add(intoResourceId, new Modules.Acquisition.Models.Input.AcquisitionLeadAddInputModel
                {
                    Kind = lead.Kind,
                    Value = lead.Value,
                    Origin = lead.Origin,
                    Note = lead.Note
                });
            }
            catch (Exception ex)
            {
                // A lead that cannot move is not worth failing the merge over — the resource it
                // pointed at is about to stop existing either way.
                logger.LogWarning(ex, "[MatchSuggestion] Could not move lead {Value} to resource {ResourceId}",
                    lead.Value, intoResourceId);
            }
        }
    }

    private async Task MoveCollectionMemberships(int fromResourceId, int intoResourceId,
        CancellationToken ct)
    {
        var memberships = await collectionMappings.GetByResourceId(fromResourceId);

        foreach (var membership in memberships)
        {
            await collectionMappings.Add(membership.CollectionId, [intoResourceId],
                membership.Origin ?? CollectionMembershipOrigin.Manual, membership.SubscriptionId, ct);
        }

        if (memberships.Count > 0)
        {
            await collectionMappings.RemoveByResourceIds([fromResourceId], ct);
        }
    }

    private async Task<Dictionary<int, string?>> ReadNames(IReadOnlyCollection<int> resourceIds)
    {
        var names = new Dictionary<int, string?>();
        var ids = resourceIds.ToList();

        foreach (var value in await reservedPropertyValues.GetAll(v =>
                     ids.Contains(v.ResourceId) && v.Name != null))
        {
            if (!names.ContainsKey(value.ResourceId) && !string.IsNullOrEmpty(value.Name))
            {
                names[value.ResourceId] = value.Name;
            }
        }

        foreach (var resource in await resources.GetByKeys(resourceIds.ToArray()))
        {
            names.TryAdd(resource.Id, resource.DisplayName ?? resource.FileName);
        }

        return names;
    }
}
