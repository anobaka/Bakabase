using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Identity;
using Bakabase.Abstractions.Components.Text;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Models.Input;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services;

/// <inheritdoc />
public class PlaceholderResourceService : IPlaceholderResourceService
{
    private readonly IResourceService _resourceService;
    private readonly IResourceSourceLinkService _sourceLinkService;
    private readonly IReservedPropertyValueService _reservedPropertyValueService;
    private readonly IAcquisitionLeadService _acquisitionLeadService;
    private readonly ITextOps _textOps;
    private readonly Dictionary<ResourceSource, IExternalIdentityLookup> _lookups;
    private readonly ISharedUrlTitleResolver? _sharedUrlTitleResolver;
    private readonly IResourceMatchSuggestionService? _matchSuggestionService;
    private readonly ILogger<PlaceholderResourceService> _logger;

    /// <summary>
    /// How many "is this the same thing?" questions one new resource is allowed to raise. A title
    /// that looks a bit like thirty others is not a match, it is a common word.
    /// </summary>
    private const int MaxSuggestionsPerResource = 3;

    public PlaceholderResourceService(
        IResourceService resourceService,
        IResourceSourceLinkService sourceLinkService,
        IReservedPropertyValueService reservedPropertyValueService,
        IAcquisitionLeadService acquisitionLeadService,
        ITextOps textOps,
        IEnumerable<IExternalIdentityLookup> lookups,
        ILogger<PlaceholderResourceService> logger,
        ISharedUrlTitleResolver? sharedUrlTitleResolver = null,
        IResourceMatchSuggestionService? matchSuggestionService = null)
    {
        _resourceService = resourceService;
        _sourceLinkService = sourceLinkService;
        _reservedPropertyValueService = reservedPropertyValueService;
        _acquisitionLeadService = acquisitionLeadService;
        _textOps = textOps;
        _logger = logger;
        _sharedUrlTitleResolver = sharedUrlTitleResolver;
        _matchSuggestionService = matchSuggestionService;
        _lookups = lookups.ToDictionary(l => l.Source, l => l);
    }

    public async Task<PlaceholderResourceResult> CreateByTitle(string title, CancellationToken ct = default)
    {
        var trimmed = title.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A resource needs a name.", nameof(title));
        }

        var matches = await FindByName(trimmed);
        if (matches.Exact.HasValue)
        {
            return new PlaceholderResourceResult(matches.Exact.Value, false, trimmed);
        }

        var resource = ResourceFactory.CreateWithoutIdentity(trimmed);
        await _resourceService.AddOrPutRange([resource]);
        await WriteName(resource.Id, PropertyValueScope.Manual, trimmed);
        await Suggest(resource.Id, matches, ct);

        return new PlaceholderResourceResult(resource.Id, true, trimmed);
    }

    public async Task<PlaceholderResourceResult> CreateOrMatchByExternalIdentity(ResourceSource source,
        string sourceKey, KnownItemDetail? known = null, CancellationToken ct = default)
    {
        var key = sourceKey.Trim();
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("An external identity needs a key.", nameof(sourceKey));
        }

        // The identity is the strongest possible match: two resources carrying the same source link
        // are the same work by definition.
        var existingId = await _sourceLinkService.FindResourceBySourceLinks([(source, key)]);
        if (existingId.HasValue)
        {
            return new PlaceholderResourceResult(existingId.Value, false, await ReadName(existingId.Value));
        }

        List<string>? coverUrls = known?.CoverUrls;
        var name = known?.Title?.Trim();

        // Only ask the platform for what the caller did not already read. A source that just
        // listed twenty works knows all twenty titles.
        if (string.IsNullOrEmpty(name) && _lookups.TryGetValue(source, out var lookup))
        {
            try
            {
                var detail = await lookup.Lookup(key, ct);

                name = detail?.Title?.Trim();
                coverUrls ??= detail?.CoverUrls;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Knowing what you are missing does not depend on the platform being up.
                _logger.LogWarning(ex, "[Placeholder] Could not read {Source} {SourceKey}; creating the resource anyway",
                    source, key);
            }
        }

        var title = string.IsNullOrEmpty(name) ? key : name;

        // No title matching here, near or exact: an identity is an identity. A platform listing a
        // work under a name something else here happens to share is not evidence about anything,
        // and scanning every name in the library per listed work would make a two-hundred-work
        // circle page an expensive sync for no answer.
        var resource = ResourceFactory.CreateForExternalIdentity(source, key, title, coverUrls,
            metadataJson: known?.MetadataJson);
        await _resourceService.AddOrPutRange([resource]);

        // The source's own scope, so a later sync from that platform updates its own value instead
        // of fighting with something the user typed.
        await WriteName(resource.Id, source.GetPropertyValueScope(), title);

        return new PlaceholderResourceResult(resource.Id, true, title);
    }

    public async Task<PlaceholderResourceResult> CreateOrMatchBySharedUrl(string url,
        KnownItemDetail? known = null, CancellationToken ct = default)
    {
        var trimmed = url.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A shared link cannot be empty.", nameof(url));
        }

        // A shared page that happens to be a store page is an identity after all — treat it as one
        // rather than as an anonymous link.
        if (ExternalIdentityParser.TryExtract(trimmed, out var source, out var sourceKey))
        {
            return await CreateOrMatchByExternalIdentity(source, sourceKey, known, ct);
        }

        // The link is already attached somewhere: that resource is what this link is about.
        var existingLead = await _acquisitionLeadService.FindByValue(AcquisitionLeadKind.SharedPage, trimmed);
        if (existingLead != null)
        {
            return new PlaceholderResourceResult(existingLead.ResourceId, false,
                await ReadName(existingLead.ResourceId));
        }

        var title = known?.Title?.Trim();

        if (string.IsNullOrWhiteSpace(title) && _sharedUrlTitleResolver != null)
        {
            try
            {
                title = await _sharedUrlTitleResolver.ResolveTitle(trimmed, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "[Placeholder] Could not read the title of {Url}", trimmed);
            }
        }

        var name = string.IsNullOrWhiteSpace(title) ? trimmed : title.Trim();

        // Only a title read from the page is worth matching on. Falling back to the URL as a name
        // is fine for display, but matching two resources because they share a URL-shaped name
        // would be nonsense.
        var matches = string.IsNullOrWhiteSpace(title)
            ? new NameMatches(null, [])
            : await FindByName(name);
        var resourceId = matches.Exact ?? 0;
        var created = false;

        if (matches.Exact == null)
        {
            var resource = ResourceFactory.CreateWithoutIdentity(name);
            await _resourceService.AddOrPutRange([resource]);
            await WriteName(resource.Id, PropertyValueScope.Synchronization, name);
            resourceId = resource.Id;
            created = true;

            await Suggest(resourceId, matches, ct);
        }

        var addResult = await _acquisitionLeadService.Add(resourceId, new AcquisitionLeadAddInputModel
        {
            Kind = AcquisitionLeadKind.SharedPage,
            Value = trimmed,
            Origin = AcquisitionLeadOrigin.User
        });
        if (addResult.Lead == null)
        {
            _logger.LogWarning(
                "[Placeholder] {Url} is already attached to resource {ResourceId}, leaving it there",
                trimmed, addResult.ConflictingResourceId);
        }

        return new PlaceholderResourceResult(resourceId, created, name);
    }

    /// <summary>
    /// Finds the resource already known by this name, if there is one — and, failing that, the ones
    /// close enough to be worth asking about.
    /// <para>
    /// The exact side is exact once both sides are trimmed and lower-cased, and the incoming title
    /// is tried both as typed and cleaned through the vocabulary. The stored side is deliberately
    /// not cleaned: that would be one vocabulary pass per existing resource on every lookup, and an
    /// exact match is what makes reuse explainable — a near-match silently reusing the wrong
    /// resource is worse than a duplicate.
    /// </para>
    /// <para>
    /// Which is why what merely looks alike does not reuse anything: it comes back as a suggestion,
    /// and a person says whether the two are one thing.
    /// </para>
    /// </summary>
    private async Task<NameMatches> FindByName(string title)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { title.Trim() };
        try
        {
            var cleaned = (await _textOps.Clean(title)).Trim();
            if (!string.IsNullOrEmpty(cleaned))
            {
                candidates.Add(cleaned);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Placeholder] Could not normalize '{Title}'; matching on it as typed", title);
        }

        var near = new List<ResourceMatchCandidate>();

        void Consider(int resourceId, string name)
        {
            // Every name in the library passes through here, so the expensive comparison is worth
            // guarding: two names of wildly different lengths cannot score anywhere near the
            // threshold, and the raw lengths say that without normalizing anything.
            if (name.Length * 2 < title.Length || title.Length * 2 < name.Length)
            {
                return;
            }

            if (!TitleSimilarity.IsWorthConfirming(title, name, out var score))
            {
                return;
            }

            near.Add(new ResourceMatchCandidate(resourceId, score, $"\"{name}\""));
        }

        var named = await _reservedPropertyValueService.GetAll(v => v.Name != null);
        foreach (var value in named)
        {
            if (string.IsNullOrEmpty(value.Name))
            {
                continue;
            }

            if (candidates.Contains(value.Name.Trim()))
            {
                return new NameMatches(value.ResourceId, []);
            }

            Consider(value.ResourceId, value.Name);
        }

        // A resource that was never named still answers to its file name.
        foreach (var resource in await _resourceService.GetAll())
        {
            if (!resource.HasLocalPath)
            {
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(resource.Path!);
            if (string.IsNullOrEmpty(fileName))
            {
                continue;
            }

            if (candidates.Contains(fileName))
            {
                return new NameMatches(resource.Id, []);
            }

            Consider(resource.Id, fileName);
        }

        return new NameMatches(null,
            near.GroupBy(c => c.CandidateResourceId)
                .Select(g => g.MaxBy(c => c.Score)!)
                .OrderByDescending(c => c.Score)
                .Take(MaxSuggestionsPerResource)
                .ToList());
    }

    /// <summary>
    /// Writes down what the resource just created might already be.
    /// </summary>
    private async Task Suggest(int resourceId, NameMatches matches, CancellationToken ct)
    {
        if (_matchSuggestionService == null || matches.Near.Count == 0)
        {
            return;
        }

        try
        {
            await _matchSuggestionService.Suggest(resourceId, matches.Near, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not being able to ask a question must not stop the resource from existing.
            _logger.LogWarning(ex, "[Placeholder] Could not record what resource {ResourceId} might already be",
                resourceId);
        }
    }

    /// <param name="Exact">The resource this title already names, when one does.</param>
    /// <param name="Near">
    /// The ones close enough to ask about — empty whenever <paramref name="Exact"/> is set, because
    /// a resource that was found needs nothing confirmed.
    /// </param>
    private record NameMatches(int? Exact, IReadOnlyList<ResourceMatchCandidate> Near);

    private async Task<string?> ReadName(int resourceId)
    {
        var values = await _reservedPropertyValueService.GetAll(v => v.ResourceId == resourceId);
        return values.Select(v => v.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n));
    }

    private async Task WriteName(int resourceId, PropertyValueScope scope, string name)
    {
        var existing = await _reservedPropertyValueService.GetAll(
            v => v.ResourceId == resourceId && v.Scope == (int)scope);
        var value = existing.FirstOrDefault();
        if (value == null)
        {
            await _reservedPropertyValueService.Add(new ReservedPropertyValue
            {
                ResourceId = resourceId,
                Scope = (int)scope,
                Name = name
            });
            return;
        }

        if (value.Name != name)
        {
            value.Name = name;
            await _reservedPropertyValueService.Update(value);
        }
    }
}
