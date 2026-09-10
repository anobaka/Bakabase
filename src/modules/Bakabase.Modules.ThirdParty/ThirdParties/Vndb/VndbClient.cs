using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Vndb.Models;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Vndb;

/// <summary>
/// VNDB, through its public query API.
/// <para>
/// The most reliable place a visual novel series is written down, and one of the few metadata
/// authorities that answers in JSON and needs no account for reading. Nobody holds files there,
/// so what it lists is a catalog: members show as missing until a lead turns up.
/// </para>
/// </summary>
public class VndbClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    private const string QueryUrl = "https://api.vndb.org/kana/vn";

    /// <summary>How many pages to walk before stopping. A prolific producer has a few hundred works.</summary>
    private const int MaxPages = 20;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Everything a developer has made, oldest first as VNDB orders it.</summary>
    /// <param name="producerId">A producer id, <c>p1</c> shaped.</param>
    public async Task<List<VndbVisualNovel>> GetByDeveloperAsync(string producerId,
        CancellationToken ct = default)
    {
        var all = new List<VndbVisualNovel>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var response = await QueryAsync(VndbRequests.ByDeveloper(producerId, page), ct);

            if (response == null) break;

            all.AddRange(response.Results);

            // VNDB says whether another page exists rather than making the caller guess from a
            // short page — a full last page is otherwise indistinguishable from a full middle one.
            if (!response.More) break;
        }

        return all;
    }

    /// <summary>
    /// Everything VNDB says belongs with one visual novel.
    /// </summary>
    /// <param name="vnId">A visual novel id, <c>v17</c> shaped.</param>
    /// <param name="relations">
    /// Which relations to keep, in VNDB's own words (<c>seq</c>, <c>preq</c>, <c>ser</c>…). Empty
    /// keeps every kind, which for a long-running franchise is more than anyone wants.
    /// </param>
    /// <param name="officialOnly">
    /// Drop fan works and unofficial ports. On by default: "the series" almost never means those,
    /// and a collection they filled would be a chore to prune.
    /// </param>
    public async Task<List<VndbRelation>> GetRelationsAsync(string vnId, IReadOnlyList<string>? relations,
        bool officialOnly = true, CancellationToken ct = default)
    {
        var response = await QueryAsync(VndbRequests.Relations(vnId), ct);
        var subject = response?.Results.FirstOrDefault();

        if (subject?.Relations is not {Count: > 0} found) return [];

        var wanted = relations?.Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return found
            .Where(r => !string.IsNullOrEmpty(r.Id))
            .Where(r => !officialOnly || r.RelationOfficial)
            .Where(r => wanted is not {Count: > 0} || (r.Relation != null && wanted.Contains(r.Relation)))
            .ToList();
    }

    /// <summary>One visual novel, for putting a name to an id.</summary>
    public async Task<VndbVisualNovel?> GetAsync(string vnId, CancellationToken ct = default) =>
        (await QueryAsync(VndbRequests.One(vnId), ct))?.Results.FirstOrDefault();

    private async Task<VndbQueryResponse?> QueryAsync(string body, CancellationToken ct)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync(QueryUrl, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            // VNDB answers a malformed filter with 400 and a plain-text reason, which is worth
            // seeing: it means a target somebody typed does not mean what they thought.
            Logger.LogWarning("[VNDB] {Status}: {Reason}", response.StatusCode,
                await response.Content.ReadAsStringAsync(ct));

            return null;
        }

        return JsonSerializer.Deserialize<VndbQueryResponse>(await response.Content.ReadAsStringAsync(ct),
            Json);
    }
}
