using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>
/// The data sync feed a node holding a <c>datasync.read</c> grant reads (§7.5): head, snapshot manifest and pages.
/// Only a <c>datasync.read</c> grant reaches it, and only while definitions sharing is on (§7.3). Every query value
/// is validated here before the feed source sees it; the source's refusals keep the federation error shape.
/// </summary>
/// <remarks>
/// It answers from this node's own feed, for the grant's subject only, and never forwards a read (§7.7, D03): nothing
/// in a request names another reader, and nothing here reaches another node (<c>DataSyncGrantBoundaryTests</c>).
/// </remarks>
[ApiController]
[Route("federation/v1/export/datasync")]
[FederationEndpoint(FederationEndpointKind.Export, Scope = FederationScopes.DataSyncRead)]
public sealed class DataSyncNodeController(FederationPeerService peers) : FederationControllerBase
{
    [HttpGet("head")]
    [SwaggerOperation(OperationId = "GetFederationDataSyncHead")]
    [ProducesResponseType(typeof(DataSyncFeedHead), 200)]
    public async Task<IActionResult> Head([FromQuery] string? mode, [FromQuery] string? since,
        [FromQuery] string? actor, [FromQuery] string? state, CancellationToken ct)
    {
        var query = DataSyncFeedQueryString.Parse(mode, since, actor, state);
        if (Source is not { } source) return NotAvailable();
        return await FeedAsync(async () =>
            FederationResult(await source.GetHeadAsync(await ReaderAsync(ct), query, ct)));
    }

    [HttpGet("manifest")]
    [SwaggerOperation(OperationId = "CreateFederationDataSyncSnapshot")]
    [ProducesResponseType(typeof(DataSyncFeedManifest), 200)]
    public async Task<IActionResult> Manifest([FromQuery] string? mode, [FromQuery] string? since,
        [FromQuery] string? actor, [FromQuery] string? state, CancellationToken ct)
    {
        var query = DataSyncFeedQueryString.Parse(mode, since, actor, state);
        if (Source is not { } source) return NotAvailable();
        return await FeedAsync(async () =>
            FederationResult(await source.CreateSnapshotAsync(await ReaderAsync(ct), query, ct)));
    }

    /// <summary>
    /// One precomputed page's raw canonical bytes. Never through <see cref="FederationControllerBase.FederationResult"/>,
    /// whose depth limit a deep multilevel property exceeds (F61).
    /// </summary>
    /// <param name="since">The served since of the kind, 0..2^53. A string, parsed here, so a malformed one is refused
    /// in the federation error shape like every other value, not by model binding.</param>
    [HttpGet("changes")]
    [SwaggerOperation(OperationId = "ReadFederationDataSyncChanges")]
    public async Task<IActionResult> Changes([FromQuery] string? snapshot, [FromQuery] string? kind,
        [FromQuery] string? since, [FromQuery] string? cursor, CancellationToken ct)
    {
        if (!NodeRequestSignature.IsIdentifier(snapshot))
            throw DataSyncFeedQueryString.Invalid("snapshot");
        if (!DataSyncFeedQueryString.IsKind(kind)) throw DataSyncFeedQueryString.Invalid("kind");
        if (!DataSyncFeedQueryString.TryParseSeq(since, out var sinceSeq))
            throw DataSyncFeedQueryString.Invalid("since");
        if (cursor != null && !NodeRequestSignature.IsIdentifier(cursor)) throw DataSyncFeedQueryString.Invalid("cursor");
        if (Source is not { } source) return NotAvailable();
        return await FeedAsync(async () =>
        {
            var page = await source.GetPageAsync(await ReaderAsync(ct), snapshot!, kind!, sinceSeq, cursor, ct);
            ct.ThrowIfCancellationRequested();
            return File(page, "application/json");
        });
    }

    /// <summary>The feed lands with the persistence layer; until it is registered this node says it has none.</summary>
    private IDataSyncFeedSource? Source => HttpContext.RequestServices.GetService<IDataSyncFeedSource>();

    private ContentResult NotAvailable() => FederationResult(new
    {
        code = "NotImplemented", message = "The data sync feed is not available on this node yet.", retryable = false
    }, 501);

    /// <summary>Who is reading: the grant's subject, by the name this device knows it by.</summary>
    private async Task<DataSyncReader> ReaderAsync(CancellationToken ct)
    {
        var principal = FederationHttpContext.GetNodePrincipal(HttpContext) ??
                        throw new FederationAccessException("NodeAuthenticationRequired", 401,
                            "A direct node read authorization is required.");
        return new DataSyncReader(principal.SubjectNodeId, principal.GrantId,
            await peers.GetPeerNameAsync(principal.SubjectNodeId, ct) ?? principal.SubjectNodeId);
    }

    /// <summary>The source's refusals as federation errors, with its own retry advice.</summary>
    private async Task<IActionResult> FeedAsync(Func<Task<IActionResult>> read)
    {
        try
        {
            return await read();
        }
        catch (DataSyncFeedException e)
        {
            if (e.RetryAfterSeconds is { } seconds)
                Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            return FederationResult(new { code = e.Code, message = FeedMessage(e.Code), retryable = e.Retryable },
                e.Status);
        }
    }

    private static string FeedMessage(string code) => code switch
    {
        "Busy" => "The source is busy. Try again shortly.",
        "TooManySnapshots" => "Snapshots were asked for too often. Try again shortly.",
        "SnapshotTooLarge" => "The source offers more than one sync can carry.",
        "SnapshotExpired" => "The snapshot expired. Ask for a new one.",
        "SnapshotMismatch" => "The page does not belong to this snapshot as it was served.",
        "SourceRestorePending" => "The source waits for a decision after a restore.",
        "UnknownKind" => "The source does not publish this kind.",
        _ => "The source refused this read."
    };
}

/// <summary>
/// The query values of the feed's head and manifest (§7.5): <c>mode</c>, <c>since</c> (up to 16 <c>kind:seq</c>
/// pairs, comma-separated), <c>actor</c> and <c>state</c>. The reader side writes them with <see cref="FormatSince"/>.
/// </summary>
public static class DataSyncFeedQueryString
{
    public const int MaxSincePairs = 16;
    public const long MaxSeq = 1L << 53;
    private static readonly Regex KindPattern = new("^[a-z][A-Za-z0-9]{1,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex StatePattern = new("^[A-Za-z0-9:]{1,64}$", RegexOptions.CultureInvariant);

    public static bool IsKind(string? kind) => kind != null && KindPattern.IsMatch(kind);
    public static bool IsSeq(long seq) => seq is >= 0 and <= MaxSeq;

    /// <summary>A sequence number as a query carries it: digits only, 0..2^53.</summary>
    public static bool TryParseSeq(string? value, out long seq) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out seq) && IsSeq(seq);

    public static string FormatSince(IReadOnlyDictionary<string, long> since) =>
        string.Join(',', since.Select(p => $"{p.Key}:{p.Value.ToString(CultureInfo.InvariantCulture)}"));

    /// <exception cref="FederationAccessException"><c>InvalidFeedQuery</c> (400) naming the value.</exception>
    public static DataSyncFeedQuery Parse(string? mode, string? since, string? actor, string? state)
    {
        if (mode != null && mode is not ("follow" or "twoWay")) throw Invalid("mode");
        var cursors = new Dictionary<string, long>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(since))
        {
            var pairs = since.Split(',');
            if (pairs.Length > MaxSincePairs) throw Invalid("since");
            foreach (var pair in pairs)
            {
                var separator = pair.IndexOf(':');
                if (separator <= 0 || !IsKind(pair[..separator]) || !TryParseSeq(pair[(separator + 1)..], out var seq) ||
                    !cursors.TryAdd(pair[..separator], seq))
                    throw Invalid("since");
            }
        }
        if (actor != null && !DataSyncActorId.IsValid(actor)) throw Invalid("actor");
        if (state != null && !StatePattern.IsMatch(state)) throw Invalid("state");
        return new DataSyncFeedQuery(mode, cursors, actor, state);
    }

    internal static FederationAccessException Invalid(string name) =>
        new("InvalidFeedQuery", 400, $"The feed query value '{name}' is invalid.");
}
