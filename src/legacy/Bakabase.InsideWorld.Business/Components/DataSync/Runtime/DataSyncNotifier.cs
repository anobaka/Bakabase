using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.Notification.Abstractions.Models.Domain;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// Data sync's notifications (§9.4), on a desktop only: a headless install has nobody to tell, and its decisions
/// reach people through its readers' attention lines instead (§7.5.1). Sources are <c>DataSync</c> for this device and
/// <c>DataSync:{peerNodeId}</c> for one peer; titles never quote a name. At most one notification goes out per link
/// per cycle, and automatic applies announce nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>One per link per cycle.</b> A link's cycle is its fetch half and, when that staged a pull, the pull's apply
/// (§8.10.2); the fetcher says where it starts and where its fetch half ends. Within it, the cases that ask for
/// something — a pause, a review ready, decisions, the approver's first sync — go out as they happen. The two that only
/// inform — a headless source's waiting decisions and follow overrides — wait for the end of the cycle and go out only
/// if nothing else did, so a first link to a NAS that already holds decisions says "ready to review" alone. Two cases
/// that ask for something in one cycle (a review for added kinds while the other kinds' pull opens decisions) both go
/// out: dropping either would leave something unannounced.
/// </para>
/// <para>
/// <b>Read state follows the items.</b> The notification that announces new items is recorded on them
/// (<c>NotificationId</c>); once every item it announced has closed — here or elsewhere — it is marked read. Each
/// payload carries <c>route</c>, which the notification center opens, and <c>case</c>, which the "at most one per …"
/// rules look for.
/// </para>
/// <para>
/// Everything runs after the change is stored and never inside a transaction; a failure is logged by the caller and
/// never undoes the change. Calls are serialized, so two events never both find "nothing sent yet".
/// </para>
/// </remarks>
public sealed class DataSyncNotifier
{
    public const string Source = "DataSync";

    public const string NeedsYouCase = "needsYou";
    public const string FollowOverrideCase = "followOverride";
    public const string PausedCase = "paused";
    public const string RestoreCase = "restore";
    public const string ReviewReadyCase = "reviewReady";
    public const string FirstSyncCase = "firstSync";
    public const string AttentionCase = "attention";
    public const string NewReaderCase = "newReader";

    /// <summary><c>DataSyncMergeNoteCodes.FollowOverride</c>: fields that took the peer's value (§8.1). Args: <c>count</c>.</summary>
    public const string FollowOverrideNote = "followOverride";

    /// <summary>A new "needs you" notification waits while an unread one from the same peer is younger than this.</summary>
    public static readonly TimeSpan NeedsYouQuietPeriod = TimeSpan.FromHours(1);

    /// <summary>Follow overrides and a source's attention are announced at most once a day per link.</summary>
    public static readonly TimeSpan DailyPeriod = TimeSpan.FromDays(1);

    /// <summary>New readers are looked for at most this often.</summary>
    public static readonly TimeSpan ReaderCheckInterval = TimeSpan.FromMinutes(1);

    private const int SearchPageSize = 50;

    /// <summary>The informing cases, in the order one gives way to the other when both wait in one cycle.</summary>
    private const int AttentionRank = 1;

    private const int FollowOverrideRank = 2;

    private readonly IServiceScopeFactory _scopes;
    private readonly IServiceProvider _root;
    private readonly IDataSyncClock _clock;
    private readonly SemaphoreSlim _lock = new(1, 1);
    /// <summary>Link → the kinds its announced review is for, while the link still waits for that review.</summary>
    private readonly ConcurrentDictionary<int, string> _reviewsAnnounced = new();
    private readonly ConcurrentDictionary<string, byte> _approved = new(StringComparer.Ordinal);
    private readonly Dictionary<int, LinkCycle> _cycles = new();
    private DateTime? _restoreAnnounced;
    private DateTime? _lastSweepUtc;
    private DateTime? _lastReaderCheckUtc;

    /// <param name="root">
    /// The link service is resolved when needed: it reports to the observer this notifier serves, so taking it in the
    /// constructor would be a cycle.
    /// </param>
    public DataSyncNotifier(IServiceScopeFactory scopes, IServiceProvider root, IDataSyncClock clock)
    {
        _scopes = scopes;
        _root = root;
        _clock = clock;
    }

    private DataSyncLinkService Links => _root.GetRequiredService<DataSyncLinkService>();

    public static string SourceOf(string peerNodeId) => $"{Source}:{peerNodeId}";

    /// <summary>
    /// This device approved the peer's request (§9.4: "not sent for a peer this device approved"): its first read is
    /// not announced as a device that started syncing on its own.
    /// </summary>
    public void NoteApproved(string peerNodeId) => _approved[peerNodeId] = 0;

    // ---- events ------------------------------------------------------------------------------------------------

    /// <summary>
    /// After a pull or a re-merge was applied: the approver's first pull reports its counts (§8.3); otherwise new
    /// decisions of the cycle, else fields a followed peer overrode. Items closed by it may settle notifications.
    /// </summary>
    public async Task AutoSyncAppliedAsync(DataSyncLinkDbModel link, DataSyncAutoSyncOutcome outcome, bool firstSync,
        CancellationToken ct)
    {
        await RunAsync(async sp =>
        {
            if (firstSync)
            {
                await FirstSyncAsync(sp, link, outcome, ct);
            }
            else if (outcome.NewInboxItems == 0 || !await NeedsYouAsync(sp, link, ct))
            {
                var overridden = outcome.Notes.Where(n => n.Code == FollowOverrideNote)
                    .Sum(n => n.Args?.GetValueOrDefault("count") is { } c &&
                              int.TryParse(c, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                        ? count
                        : 1);
                if (overridden > 0)
                {
                    await InformAsync(sp, link.Id, FollowOverrideRank,
                        (s, t) => FollowOverrideAsync(s, link, overridden, t), ct);
                }
            }

            // The apply ends the link's cycle (§8.10.2).
            await CloseCycleAsync(sp, link.Id, ct);
        }, ct);
        if (outcome.ClosedInboxItems > 0 || outcome.ClosedItemIds.Count > 0) await SweepAsync(ct);
    }

    /// <summary>
    /// A link paused (§8.7): one notification for a breaker, never for the person's own pause. A local restore (B6)
    /// is announced once per detection, however many links it paused.
    /// </summary>
    public Task LinkPausedAsync(DataSyncLinkDbModel link, CancellationToken ct) => RunAsync(async sp =>
    {
        if (link.PausedReason is DataSyncPauseReason.LocalRestoreDetected or DataSyncPauseReason.LocalRestoreSuspected)
        {
            await RestoreAsync(sp, await sp.GetRequiredService<IDataSyncStore>().GetLocalStateAsync(ct), ct);
            return;
        }

        if (PauseReasonOf(sp, link) is not { } reason) return;
        await CreateAsync(sp, SourceOf(link.PeerNodeId), PausedCase, "Paused", [link.PeerName, reason], [],
            LinkRoute(link.Id), AppNotificationSeverity.Warning, ct);
        MarkNotified(link.Id);
    }, ct);

    /// <summary>
    /// A first-link review was staged (§8.3 step 3), routed to the link: once per link and set of kinds under review
    /// while the link waits for it. A review staged again for the same kinds — the last one idled out, "Fetch again",
    /// a restart — is the same question and is not announced again; nor while an unread announcement of it is still
    /// in the notification center, so a restart does not repeat it either. The link's route opens whatever review is
    /// current.
    /// </summary>
    public Task ReviewReadyAsync(DataSyncLinkDbModel link, DataSyncReviewEntry review, CancellationToken ct) =>
        RunAsync(async sp =>
        {
            var kinds = string.Join(",", review.Pull.Kinds.Select(k => k.Kind).Distinct(StringComparer.Ordinal)
                .OrderBy(k => k, StringComparer.Ordinal));
            if (_reviewsAnnounced.TryGetValue(link.Id, out var announced) && announced == kinds) return;
            _reviewsAnnounced[link.Id] = kinds;
            var source = SourceOf(link.PeerNodeId);
            var linkId = Id(link.Id);
            if ((await SearchAsync(sp, source, true)).Any(n => CaseOf(n) == ReviewReadyCase &&
                                                              StringOf(PayloadOf(n)?["link"]) == linkId &&
                                                              StringOf(PayloadOf(n)?["kinds"]) == kinds))
            {
                return;
            }

            await CreateAsync(sp, source, ReviewReadyCase, "ReviewReady", [link.PeerName], [],
                $"/data-sync?link={linkId}&review=1", AppNotificationSeverity.Info, ct,
                new Dictionary<string, string> { ["link"] = linkId, ["kinds"] = kinds });
            MarkNotified(link.Id);
        }, ct);

    /// <summary>
    /// A link's peer facts changed: a headless source whose attention shows open decisions, paused links or a pending
    /// restore is announced at most once a day per source (§9.4, example N) — within a cycle, only if nothing else is
    /// announced for the link in it. A stopped link may have closed items.
    /// </summary>
    public async Task LinkChangedAsync(DataSyncLinkDbModel link, CancellationToken ct)
    {
        // A link that no longer waits for a review — its review applied, or the link stopped — may be announced again
        // when it next needs one.
        if (link.State == DataSyncLinkState.Stopped ||
            (link.State != DataSyncLinkState.AwaitingReview && link.GetKindsAwaitingFirstContact().Count == 0))
        {
            _reviewsAnnounced.TryRemove(link.Id, out _);
        }

        await RunAsync(async sp =>
        {
            if (link.GetPeerAttention() is not { Headless: true } attention) return;
            var waiting = attention.OpenDecisions + attention.PausedLinks + (attention.RestorePending ? 1 : 0);
            if (waiting == 0) return;
            await InformAsync(sp, link.Id, AttentionRank, (s, t) => AttentionAsync(s, link, waiting, t), ct);
        }, ct);
        if (link.State == DataSyncLinkState.Stopped) await SweepAsync(ct);
    }

    /// <summary>
    /// The fetch half of a link's cycle begins (§8.10.2); what the link's previous cycle still held back — its pull
    /// was dropped or replaced before an apply ended it — goes out first.
    /// </summary>
    public Task LinkCycleStartedAsync(int linkId, CancellationToken ct) => RunAsync(async sp =>
    {
        await CloseCycleAsync(sp, linkId, ct);
        _cycles[linkId] = new LinkCycle();
    }, ct);

    /// <summary>
    /// The fetch half of a link's cycle ended: without a staged pull that is the end of the cycle; with one, its apply
    /// ends it (<see cref="AutoSyncAppliedAsync"/>).
    /// </summary>
    public Task LinkFetchEndedAsync(int linkId, bool applyFollows, CancellationToken ct) => RunAsync(async sp =>
    {
        if (!applyFollows) await CloseCycleAsync(sp, linkId, ct);
    }, ct);

    /// <summary>A link was reset: what its cycle held back is about a link that no longer exists.</summary>
    public Task LinkRemovedAsync(int linkId, CancellationToken ct) => RunAsync(_ =>
    {
        _cycles.Remove(linkId);
        _reviewsAnnounced.TryRemove(linkId, out string? _);
        return Task.CompletedTask;
    }, ct);

    /// <summary>
    /// The scheduler's tick (§8.2): a restore detection is announced once, and — at most once a minute — a device that
    /// started reading this one on its own (a code it redeemed) is announced once (§9.4).
    /// </summary>
    public Task LocalStateSeenAsync(DataSyncLocalStateDbModel? local, CancellationToken ct) => RunAsync(async sp =>
    {
        await RestoreAsync(sp, local, ct);
        var now = _clock.UtcNow;
        if (_lastReaderCheckUtc is { } last && now - last < ReaderCheckInterval) return;
        _lastReaderCheckUtc = now;
        await NewReadersAsync(sp, ct);
    }, ct);

    /// <summary>
    /// The actor guard detected a restore (§5.6): announced at once from the local state row it wrote, rather than on
    /// the scheduler's next tick. One per detection (the tick finds it announced).
    /// </summary>
    public Task RestoreDetectedAsync(CancellationToken ct) => RunAsync(async sp =>
        await RestoreAsync(sp, await sp.GetRequiredService<IDataSyncStore>().GetLocalStateAsync(ct), ct), ct);

    /// <summary>
    /// Marks read every notification whose announced items have all closed since the last sweep (§9.3, §9.4). The
    /// first sweep of a process looks at every closed item, so nothing a restart interrupted stays unread.
    /// </summary>
    public async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        if (IsHeadless(sp)) return;
        await _lock.WaitAsync(ct);
        try
        {
            var now = _clock.UtcNow;
            // A little overlap, so an item closed by another writer's clock just before the last sweep is not missed.
            var since = _lastSweepUtc is { } last ? last - TimeSpan.FromMinutes(10) : DateTime.MinValue;
            var settled = await sp.GetRequiredService<IDataSyncStore>().GetSettledNotificationsAsync(since, ct);
            _lastSweepUtc = now;
            // Never an empty list: MarkAsReadAsync marks every unread notification then.
            if (settled.Count > 0)
                await sp.GetRequiredService<INotificationService>().MarkAsReadAsync(settled.Distinct().ToArray());
        }
        finally
        {
            _lock.Release();
        }
    }

    // ---- cases -------------------------------------------------------------------------------------------------

    /// <summary>
    /// "{0}: {1} changes need you" for the items no notification announced yet — unless an unread one from the same
    /// peer is younger than an hour. When that one announces decisions too, the items join it, so it is read only once
    /// they are all decided; otherwise they wait for the next announcement.
    /// </summary>
    private async Task<bool> NeedsYouAsync(IServiceProvider sp, DataSyncLinkDbModel link, CancellationToken ct)
    {
        var store = sp.GetRequiredService<IDataSyncStore>();
        var unannounced = await store.GetUnannouncedItemIdsAsync(link.Id, ct);
        if (unannounced.Count == 0) return false;
        var now = _clock.UtcNow;
        var recent = (await SearchAsync(sp, SourceOf(link.PeerNodeId), true))
            .FirstOrDefault(n => now - CreatedUtc(n) < NeedsYouQuietPeriod);
        if (recent is not null)
        {
            if (CaseOf(recent) is NeedsYouCase or FirstSyncCase)
                await store.SetItemsNotifiedAsync(unannounced, recent.Id, now, ct);
            return true;
        }

        var id = await CreateAsync(sp, SourceOf(link.PeerNodeId), NeedsYouCase, "NeedsYou",
            [link.PeerName, unannounced.Count], [], $"/data-sync?tab=inbox&peer={Uri.EscapeDataString(link.PeerNodeId)}",
            AppNotificationSeverity.Info, ct);
        MarkNotified(link.Id);
        await store.SetItemsNotifiedAsync(unannounced, id, now, ct);
        return true;
    }

    /// <summary>"First sync with {0}: {1} created, {2} linked, {3} need you", which also announces those items (§8.3).</summary>
    private async Task FirstSyncAsync(IServiceProvider sp, DataSyncLinkDbModel link, DataSyncAutoSyncOutcome outcome,
        CancellationToken ct)
    {
        var store = sp.GetRequiredService<IDataSyncStore>();
        var entry = outcome.ApplyLogId is { } logId ? await store.GetHistoryEntryAsync(logId, ct) : null;
        var counts = entry is null ? null : DataSyncHistoryJson.ReadCounts(entry.SummaryJson, entry.ResultJson);
        var unannounced = await store.GetUnannouncedItemIdsAsync(link.Id, ct);
        var id = await CreateAsync(sp, SourceOf(link.PeerNodeId), FirstSyncCase, "FirstSync",
            [link.PeerName, counts?.Created ?? 0, counts?.Linked ?? 0, unannounced.Count], [],
            LinkRoute(link.Id), AppNotificationSeverity.Info, ct);
        MarkNotified(link.Id);
        if (unannounced.Count > 0) await store.SetItemsNotifiedAsync(unannounced, id, _clock.UtcNow, ct);
    }

    /// <summary>
    /// "{0} has {1} changes waiting for a decision": at most once a day per source, claimed on the link row so two
    /// events never both send it.
    /// </summary>
    private async Task AttentionAsync(IServiceProvider sp, DataSyncLinkDbModel link, int waiting, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        if (link.AttentionNotifiedAtUtc is { } last && now - DataSyncViews.Utc(last) < DailyPeriod) return;
        var claimed = await Links.MutateAsync(link.Id, row =>
        {
            if (row.AttentionNotifiedAtUtc is { } at && now - DataSyncViews.Utc(at) < DailyPeriod)
                return DataSyncLinkWrite.None;
            row.AttentionNotifiedAtUtc = now;
            return DataSyncLinkWrite.Bookkeeping;
        }, ct);
        if (claimed?.AttentionNotifiedAtUtc != now) return;
        await CreateAsync(sp, SourceOf(link.PeerNodeId), AttentionCase, "Attention", [link.PeerName, waiting],
            [link.PeerName], LinkRoute(link.Id), AppNotificationSeverity.Info, ct);
    }

    /// <summary>"{1} changes on this device were replaced by {0}'s": at most one per link per day (§8.1).</summary>
    private async Task FollowOverrideAsync(IServiceProvider sp, DataSyncLinkDbModel link, int count,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var source = SourceOf(link.PeerNodeId);
        if ((await SearchAsync(sp, source, false)).Any(n =>
                CaseOf(n) == FollowOverrideCase && now - CreatedUtc(n) < DailyPeriod))
        {
            return;
        }

        await CreateAsync(sp, source, FollowOverrideCase, "FollowOverride", [link.PeerName, count], [link.PeerName],
            LinkRoute(link.Id), AppNotificationSeverity.Info, ct);
    }

    /// <summary>
    /// "Data sync is paused: this device's data looks restored" — one per detection (§9.4), however many links it
    /// paused and however often the host restarts while the choice waits: the detection time travels in the payload.
    /// </summary>
    private async Task RestoreAsync(IServiceProvider sp, DataSyncLocalStateDbModel? local, CancellationToken ct)
    {
        if (local is not { RestoreReason: not null, RestoreDetectedAtUtc: { } detected }) return;
        detected = DataSyncViews.Utc(detected);
        if (_restoreAnnounced == detected) return;
        var stamp = detected.ToString("O", CultureInfo.InvariantCulture);
        var sent = (await SearchAsync(sp, Source, false)).Any(n =>
            CaseOf(n) == RestoreCase && StringOf(PayloadOf(n)?["detectedAt"]) == stamp);
        if (!sent)
        {
            await CreateAsync(sp, Source, RestoreCase, "Restore", [], [], "/data-sync?restore=1",
                AppNotificationSeverity.Warning, ct, new Dictionary<string, string> { ["detectedAt"] = stamp });
        }

        _restoreAnnounced = detected;
    }

    /// <summary>
    /// "{0} started syncing definitions with this device" for a reader that declared a mode for the first time, once
    /// (<c>NotifiedAtUtc</c>). Not for a peer this device approved or has a link with: those it already knows about.
    /// </summary>
    private async Task NewReadersAsync(IServiceProvider sp, CancellationToken ct)
    {
        var store = sp.GetRequiredService<IDataSyncStore>();
        var readers = (await store.GetReadersAsync(ct)).Where(r => r.NotifiedAtUtc is null && r.Mode is not null)
            .ToList();
        if (readers.Count == 0) return;
        var linked = (await store.GetLinksAsync(ct)).Select(l => l.PeerNodeId).ToHashSet(StringComparer.Ordinal);
        foreach (var reader in readers)
        {
            if (!linked.Contains(reader.NodeId) && !_approved.ContainsKey(reader.NodeId))
            {
                await CreateAsync(sp, Source, NewReaderCase, "NewReader", [reader.Name], [], "/data-sync",
                    AppNotificationSeverity.Info, ct, new Dictionary<string, string> { ["peer"] = reader.NodeId });
            }

            await store.SetReaderNotifiedAsync(reader.NodeId, _clock.UtcNow, ct);
        }
    }

    // ---- one per link per cycle --------------------------------------------------------------------------------

    /// <summary>
    /// A link's cycle as the notifier sees it: whether a notification went out for the link in it, and the informing
    /// case that goes out at its end if none did.
    /// </summary>
    private sealed class LinkCycle
    {
        public bool Notified;
        public int DeferredRank;
        public Func<IServiceProvider, CancellationToken, Task>? Deferred;
    }

    private void MarkNotified(int linkId)
    {
        if (_cycles.TryGetValue(linkId, out var cycle)) cycle.Notified = true;
    }

    /// <summary>
    /// An informing case: sent at once outside a cycle; within one, held for its end, where it goes out only if
    /// nothing else did — the higher rank of two held ones.
    /// </summary>
    private async Task InformAsync(IServiceProvider sp, int linkId, int rank,
        Func<IServiceProvider, CancellationToken, Task> send, CancellationToken ct)
    {
        if (!_cycles.TryGetValue(linkId, out var cycle))
        {
            await send(sp, ct);
            return;
        }

        if (cycle.Notified || (cycle.Deferred is not null && cycle.DeferredRank > rank)) return;
        cycle.Deferred = send;
        cycle.DeferredRank = rank;
    }

    private async Task CloseCycleAsync(IServiceProvider sp, int linkId, CancellationToken ct)
    {
        if (!_cycles.Remove(linkId, out var cycle)) return;
        if (!cycle.Notified && cycle.Deferred is { } deferred) await deferred(sp, ct);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    /// <summary>The pause reason as the §11.6 state words say it; null for the person's own pauses.</summary>
    private static string? PauseReasonOf(IServiceProvider sp, DataSyncLinkDbModel link)
    {
        var localizer = sp.GetRequiredService<IBakabaseLocalizer>();
        string key;
        object[] args = [];
        switch (link.PausedReason)
        {
            case DataSyncPauseReason.PeerReset when link.PausedDetail == DataSyncLinkService.RestoredDetail:
                key = "PeerRestored";
                break;
            case DataSyncPauseReason.PeerReset:
                key = "PeerReset";
                break;
            case DataSyncPauseReason.MassDeletion:
                key = "MassDeletion";
                args = [DetailValue(link.PausedDetail, "deletions") ?? "?"];
                break;
            case DataSyncPauseReason.KindEmptied:
                key = "KindEmptied";
                args = [KindName(localizer, DetailValue(link.PausedDetail, "kind"))];
                break;
            case DataSyncPauseReason.PeerIdentityDuplicated:
                key = "PeerIdentityDuplicated";
                break;
            case DataSyncPauseReason.TooManyDecisions:
                key = "TooManyDecisions";
                break;
            default:
                return null;
        }

        return localizer[$"DataSync_Notify_PauseReason_{key}", args].Value;
    }

    /// <summary>A kind as its page names it; the id itself when this build does not know it.</summary>
    private static string KindName(IBakabaseLocalizer localizer, string? kind) =>
        kind is not null && DataSyncKindIds.All.Contains(kind) ? localizer[$"DataSync_Kind_{kind}"].Value : kind ?? "?";

    /// <summary>A value of a pause detail such as <c>deletions=182;kind=customProperty</c> (§8.7).</summary>
    private static string? DetailValue(string? detail, string name) =>
        detail?.Split(';').Select(p => p.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == name)?[1];

    private async Task<int> CreateAsync(IServiceProvider sp, string source, string @case, string textKey,
        object[] titleArgs, object[] bodyArgs, string route, AppNotificationSeverity severity, CancellationToken ct,
        IReadOnlyDictionary<string, string>? extra = null)
    {
        ct.ThrowIfCancellationRequested();
        var localizer = sp.GetRequiredService<IBakabaseLocalizer>();
        var payload = new JsonObject { ["route"] = route, ["case"] = @case };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>()) payload[key] = value;
        var record = await sp.GetRequiredService<INotificationService>().CreateAsync(new NotificationCreationInputModel
        {
            Source = source,
            Title = localizer[$"DataSync_Notify_{textKey}_Title", titleArgs].Value,
            Body = localizer[$"DataSync_Notify_{textKey}_Body", bodyArgs].Value,
            PayloadJson = payload.ToJsonString(),
            Severity = severity,
        });
        return record.Id;
    }

    private static async Task<IReadOnlyList<NotificationRecord>> SearchAsync(IServiceProvider sp, string source,
        bool unreadOnly)
    {
        var response = await sp.GetRequiredService<INotificationService>().SearchAsync(
            new NotificationSearchInputModel { Source = source, UnreadOnly = unreadOnly, PageSize = SearchPageSize });
        return response.Data ?? [];
    }

    private static JsonObject? PayloadOf(NotificationRecord notification)
    {
        if (string.IsNullOrWhiteSpace(notification.PayloadJson)) return null;
        try
        {
            return JsonNode.Parse(notification.PayloadJson) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? CaseOf(NotificationRecord notification) => StringOf(PayloadOf(notification)?["case"]);

    private static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>Notifications are stamped in local time; compared here in UTC.</summary>
    private static DateTime CreatedUtc(NotificationRecord notification) => notification.CreatedAt.Kind switch
    {
        DateTimeKind.Utc => notification.CreatedAt,
        _ => DateTime.SpecifyKind(notification.CreatedAt, DateTimeKind.Local).ToUniversalTime(),
    };

    private static string LinkRoute(int linkId) => $"/data-sync?link={Id(linkId)}";

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static bool IsHeadless(IServiceProvider sp) => sp.GetService<IDataSyncHostKind>()?.IsHeadless == true;

    /// <summary>One notification step at a time, on a desktop only.</summary>
    private async Task RunAsync(Func<IServiceProvider, Task> step, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        if (IsHeadless(sp)) return;
        await _lock.WaitAsync(ct);
        try
        {
            await step(sp);
        }
        finally
        {
            _lock.Release();
        }
    }
}
