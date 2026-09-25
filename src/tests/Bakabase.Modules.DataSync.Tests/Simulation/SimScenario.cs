using System.Text;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Tests.TestKinds;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

internal enum SimTopology { Pair, Chain, Star, Mesh }

internal enum SimModeMix { TwoWay, MixFollow, MutualFollow }

internal enum SimStepKind
{
    Create, Edit, Delete, Sync, SyncTwice, SyncConcurrentWrite, SyncAll, Resolve, Use, Values, Reorder, EntityState,
    Undo, Backup, RestoreDatabase, RestoreDirectory, Restart, StopLink, StartLink, ResetLink, Partition, Heal,
    LongPartition, Advance, StaleWrite, Include, SyncStaleWrite, Bulk, RestoreOffline, ChildrenLocal, SyncCrossed,

    // Inserted after the weighted draw (see Generate), not drawn by weight.
    UndoRelink, EditApplied,
}

/// <summary>
/// One step as data: its kind and four random arguments that the interpreter resolves against the state at the
/// time (modulo what exists). Steps therefore replay after others are removed, which is what shrinking needs.
/// </summary>
internal sealed record SimStep(SimStepKind Kind, int A, int B, int C, int D)
{
    public override string ToString() => $"{Kind}({A},{B},{C},{D})";
}

/// <summary>A scenario: the world's shape and its steps, all from one seed.</summary>
internal sealed record SimScenarioSpec(int Seed, int NodeCount, SimTopology Topology, SimModeMix Modes, IReadOnlyList<SimStep> Steps)
{
    private static readonly (SimStepKind Kind, int Weight)[] Weights =
    [
        (SimStepKind.Create, 9), (SimStepKind.Edit, 22), (SimStepKind.Delete, 4), (SimStepKind.Sync, 22),
        (SimStepKind.SyncTwice, 3), (SimStepKind.SyncConcurrentWrite, 3), (SimStepKind.SyncAll, 5),
        (SimStepKind.Resolve, 8), (SimStepKind.Use, 6), (SimStepKind.Values, 3), (SimStepKind.Reorder, 4),
        (SimStepKind.EntityState, 2), (SimStepKind.Undo, 3), (SimStepKind.Backup, 3), (SimStepKind.RestoreDatabase, 1),
        (SimStepKind.RestoreDirectory, 1), (SimStepKind.Restart, 1), (SimStepKind.StopLink, 1), (SimStepKind.StartLink, 1),
        (SimStepKind.ResetLink, 1), (SimStepKind.Partition, 2), (SimStepKind.Heal, 2), (SimStepKind.LongPartition, 1),
        (SimStepKind.Advance, 2), (SimStepKind.StaleWrite, 1), (SimStepKind.Include, 1), (SimStepKind.SyncStaleWrite, 2),
        (SimStepKind.Bulk, 1), (SimStepKind.RestoreOffline, 1), (SimStepKind.ChildrenLocal, 2),
        (SimStepKind.SyncCrossed, 3),
    ];

    public static SimScenarioSpec Generate(int seed, int maxSteps = 60)
    {
        var random = new Random(seed);
        var nodes = random.Next(2, 5);
        var topology = nodes == 2 ? SimTopology.Pair : (SimTopology)random.Next(1, 4);
        var modes = random.Next(10) switch { < 5 => SimModeMix.TwoWay, < 8 => SimModeMix.MixFollow, _ => SimModeMix.MutualFollow };
        var steps = new List<SimStep>();
        SimStep Step(SimStepKind kind) => new(kind, random.Next(1 << 20), random.Next(1 << 20), random.Next(1 << 20), random.Next(1 << 20));
        for (var i = random.Next(2, 6); i > 0; i--) steps.Add(Step(SimStepKind.Create));
        steps.Add(Step(SimStepKind.SyncAll));
        var prefix = steps.Count;
        var total = Weights.Sum(w => w.Weight);
        for (var i = random.Next(maxSteps / 3, maxSteps + 1) - steps.Count; i > 0; i--)
        {
            var pick = random.Next(total);
            foreach (var (kind, weight) in Weights)
            {
                if ((pick -= weight) >= 0) continue;
                steps.Add(Step(kind));
                break;
            }
        }

        // Steps added after the table above come from a stream of their own and are inserted among the drawn ones,
        // so a seed keeps every step it had (the seeds that found defects still start the way they did).
        var extra = new Random(seed ^ 0x2a2a2a);
        SimStep Extra(SimStepKind kind) => new(kind, extra.Next(1 << 20), extra.Next(1 << 20), extra.Next(1 << 20),
            extra.Next(1 << 20));
        foreach (var (kind, most) in new[] { (SimStepKind.UndoRelink, 1), (SimStepKind.EditApplied, 3) })
        {
            for (var n = extra.Next(most + 1); n > 0; n--)
                steps.Insert(extra.Next(prefix, steps.Count + 1), Extra(kind));
        }

        return new SimScenarioSpec(seed, nodes, topology, modes, steps);
    }
}

/// <summary>
/// Runs one scenario of the convergence simulator (§13.3): the random steps (invariants I3, I4 and I8 checked as
/// they happen), then quiescence — every partition healed, links resumed, restore choices made, sync rounds until
/// nothing changes (I9), every open item resolved by a deterministic chooser on a desktop node, rounds again — and
/// the quiescence invariants I1, I2, I5, I7 and I10. Failures are collected, never thrown.
/// </summary>
internal sealed class SimScenario
{
    private const int MaxRounds = 16;

    /// <summary>A safety net only: a bulk of fifty-one look-alike definitions raises a few hundred honest questions.</summary>
    private const int MaxChooserSteps = 4_000;

    /// <summary>The same question of one entity asked again this often on one device is a livelock.</summary>
    private const int MaxDecisionsPerQuestion = 12;

    private readonly SimScenarioSpec _spec;
    private readonly Dictionary<string, List<SimBackup>> _backups = new(StringComparer.Ordinal);

    public SimScenario(SimScenarioSpec spec)
    {
        _spec = spec;
        World = new SimWorld(spec.Seed)
        {
            WireLimits = spec.Seed % 5 == 0 ? SimWorld.SmallWireLimits : SimKinds.Limits,
        };
        Build();
    }

    public SimWorld World { get; }
    public List<string> Steps { get; } = [];
    public List<string> Failures { get; } = [];
    public int HubFallbackResolutions { get; private set; }
    public int Resolutions { get; private set; }

    /// <summary>The run hit the residual restore window (§5.6): peers may meet equal vectors with different forms.</summary>
    public bool ResidualRestore { get; private set; }

    private IReadOnlyList<SimNode> Nodes => World.Nodes;
    private IEnumerable<SimNode> Desktops => Nodes.Where(n => !n.Headless);

    // ---- shape ---------------------------------------------------------------------------------------

    private void Build()
    {
        var names = Enumerable.Range(1, _spec.NodeCount).Select(i => "PC-" + i).ToList();
        if (_spec.Topology == SimTopology.Star) names[0] = "NAS";
        foreach (var name in names) World.AddNode(name, headless: name == "NAS");

        var edges = new List<(SimNode, SimNode)>();
        switch (_spec.Topology)
        {
            case SimTopology.Pair or SimTopology.Chain:
                for (var i = 1; i < Nodes.Count; i++) edges.Add((Nodes[i - 1], Nodes[i]));
                break;
            case SimTopology.Star:
                for (var i = 1; i < Nodes.Count; i++) edges.Add((Nodes[i], Nodes[0]));
                break;
            default:
                for (var i = 0; i < Nodes.Count; i++)
                for (var j = i + 1; j < Nodes.Count; j++)
                    edges.Add((Nodes[i], Nodes[j]));
                break;
        }

        var random = new Random(_spec.Seed * 31 + 7);
        foreach (var (a, b) in edges)
        {
            switch (_spec.Modes)
            {
                case SimModeMix.MutualFollow:
                    a.Follow(b, DataSyncLinkMode.Follow);
                    b.Follow(a, DataSyncLinkMode.Follow);
                    break;
                case SimModeMix.MixFollow:
                    switch (random.Next(3))
                    {
                        case 0:
                            a.Follow(b);
                            b.Follow(a);
                            break;
                        case 1:
                            a.Follow(b, DataSyncLinkMode.Follow);
                            b.Follow(a);
                            break;
                        default:
                            // One direction only: b receives from a.
                            b.Follow(a, DataSyncLinkMode.Follow);
                            break;
                    }

                    break;
                default:
                    a.Follow(b);
                    b.Follow(a);
                    break;
            }
        }

        // §8.1: Follow cycles longer than two (A follows B, B follows C, C follows A) are not supported — they can
        // rotate values. Any pull cycle of three or more devices with a Follow hop does the same: the follower's
        // override reverts its own change after a two-way peer took it, and both three-way merges against their link
        // bases then read the other's value as the change (each result equals the remote, so both end at Max(L, R)
        // with swapped contents, and row A2 pauses them as a duplicated actor). So Follow stays off such cycles here
        // (mutual Follow works as two-way and stays); example I's shape, with no cycle, keeps it.
        while (FollowCycle() is { } cycleLink) cycleLink.Mode = cycleLink.LastMode = DataSyncLinkMode.TwoWay;

        Steps.Add($"shape: {_spec.Topology} {_spec.Modes}, " + string.Join("; ",
            Nodes.Select(n => $"{n.Name} ← {string.Join(",", n.Links.Values.Select(l => $"{l.Peer.Name}:{l.Mode}"))}")));
    }

    /// <summary>
    /// A Follow link (not half of a mutual Follow) on a directed pull cycle (reader → source) of three or more
    /// devices, or null.
    /// </summary>
    private SimLink? FollowCycle()
    {
        bool OneWayFollow(SimLink link, SimNode reader) =>
            link.Mode == DataSyncLinkMode.Follow && reader.EffectiveMode(link) == DataSyncLinkMode.Follow;

        List<(SimNode Reader, SimLink Link)>? Search(SimNode start, SimNode at, List<(SimNode Reader, SimLink Link)> path)
        {
            foreach (var link in at.Links.Values.OrderBy(l => l.Id))
            {
                if (link.Peer == start && path.Count >= 2)
                {
                    List<(SimNode Reader, SimLink Link)> cycle = [.. path, (at, link)];
                    if (cycle.Any(x => OneWayFollow(x.Link, x.Reader))) return cycle;
                    continue;
                }

                if (path.Count >= Nodes.Count || path.Any(p => p.Link.Peer == link.Peer) || link.Peer == start) continue;
                if (Search(start, link.Peer, [.. path, (at, link)]) is { } found) return found;
            }

            return null;
        }

        foreach (var node in Nodes)
        {
            if (Search(node, node, []) is { } cycle) return cycle.First(x => OneWayFollow(x.Link, x.Reader)).Link;
        }

        return null;
    }

    /// <summary>Whether a node lies on a directed pull cycle of three or more devices.</summary>
    private bool OnPullCycle(SimNode node)
    {
        bool Reaches(SimNode at, int depth, HashSet<SimNode> seen)
        {
            foreach (var link in at.Links.Values.Where(l => !l.Stopped))
            {
                if (link.Peer == node && depth >= 2) return true;
                if (link.Peer == node || !seen.Add(link.Peer)) continue;
                if (Reaches(link.Peer, depth + 1, seen)) return true;
                seen.Remove(link.Peer);
            }

            return false;
        }

        return Reaches(node, 0, [node]);
    }

    // ---- the run -------------------------------------------------------------------------------------

    public void Run(bool quiesce = true)
    {
        var index = 0;
        foreach (var step in _spec.Steps)
        {
            // Every install keeps a few recent copies of itself, so a restore always has one to go back to.
            if (index++ % 5 == 0)
            {
                foreach (var node in Nodes)
                {
                    if (!_backups.TryGetValue(node.NodeId, out var list)) _backups[node.NodeId] = list = [];
                    list.Add(node.Backup());
                    if (list.Count > 3) list.RemoveAt(0);
                }
            }

            string description;
            try
            {
                description = Execute(step);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Failures.Add($"step {step} threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                return;
            }

            Steps.Add($"{step}: {description}");
            World.Count("step:" + step.Kind + (description.StartsWith("no", StringComparison.Ordinal) ? ":noop" : ""));
            World.Clock.Advance(TimeSpan.FromMinutes(1 + (step.A + step.B) % 15));
            foreach (var node in Nodes) node.UpdateVerified();
            CheckKeyInvariant(step.ToString());
            CollectViolations(step.ToString());
            if (Failures.Count > 0) return;
        }

        if (!quiesce) return;
        try
        {
            Quiesce();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failures.Add($"quiescence threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
    }

    private string Execute(SimStep s)
    {
        var node = Nodes[s.A % Nodes.Count];
        var rows = node.Rows.Where(r => r.IsLive).OrderBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.LocalKey, StringComparer.Ordinal).ToList();
        SimRow? Row(int arg) => rows.Count == 0 ? null : rows[arg % rows.Count];
        var links = node.Links.Values.OrderBy(l => l.Id).ToList();
        SimLink? Link(int arg) => links.Count == 0 ? null : links[arg % links.Count];

        switch (s.Kind)
        {
            case SimStepKind.Create:
            {
                var kind = SimKinds.All[s.B % 3 == 0 ? 0 : 1];
                var name = kind.Names[s.C % kind.Names.Count];
                var row = node.Create(kind.Kind, kind.NewContent(new Random(s.D), name, World.NewChildId));
                return $"{node} creates {row.Kind} {row.Content}";
            }
            case SimStepKind.Edit:
            {
                if (Row(s.B) is not { } row) return "nothing to edit";
                var kind = SimKinds.Of(row.Kind);
                if (kind.RandomEdit(new Random(s.C), row.Content!, World.NewChildId) is not { } edit) return "no edit";
                var was = row.Content;
                node.EditRow(row, edit.Content);
                return $"{node} {edit.What} {was} → {edit.Content}";
            }
            case SimStepKind.Delete:
            {
                if (Row(s.B) is not { } row) return "nothing to delete";
                node.DeleteRow(row);
                return $"{node} deletes {row.Kind}:{row.LocalKey}";
            }
            case SimStepKind.Sync:
                return Link(s.B) is { } pulled ? $"{node} ← {pulled.Peer}: {node.Pull(pulled)}" : "no link";
            case SimStepKind.SyncTwice:
            {
                if (Link(s.B) is not { } link) return "no link";
                var outcome = node.Pull(link);
                if (outcome != SimPullOutcome.Applied || node.LastPull is not { } pull) return $"{node} ← {link.Peer}: {outcome}";
                var before = SimDigest.Of(node);
                var again = node.Redeliver(link, pull);
                var after = SimDigest.Of(node);
                if (before != after) Failures.Add($"I3: delivering the same snapshot twice changed {node} ({again}):\n{Diff(before, after)}");
                return $"{node} ← {link.Peer}: {outcome}, delivered twice";
            }
            case SimStepKind.SyncConcurrentWrite:
            {
                if (Link(s.B) is not { } link) return "no link";
                var hit = "";
                node.BeforeApply = (n, result) =>
                {
                    var targets = result.Batches.SelectMany(b => b.Operations.OfType<UpdateEntityOperation>()
                        .Select(u => n.LiveRow(b.Kind, u.LocalKey))).Where(r => r?.Content is not null).ToList();
                    if (targets.Count == 0) return;
                    var target = targets[s.C % targets.Count]!;
                    var kind = SimKinds.Of(target.Kind);
                    if (kind.RandomEdit(new Random(s.D), target.Content!, World.NewChildId) is { } edit)
                    {
                        n.EditRow(target, edit.Content);
                        hit = $", a request wrote {target.Kind}:{target.LocalKey} meanwhile ({edit.What})";
                    }
                };
                var outcome = node.Pull(link);
                node.BeforeApply = null;
                return $"{node} ← {link.Peer}: {outcome}{hit}";
            }
            case SimStepKind.SyncCrossed:
            {
                // Two devices that pull each other both take their snapshot before either applies (§8.5.5): each merges
                // the other's revision against its own, crosswise, which sequential pulls never do.
                if (Link(s.B) is not { } link || link.Peer.Links.GetValueOrDefault(node.NodeId) is not { } back)
                    return "no link both ways";
                var peer = link.Peer;
                var fetched = node.Fetch(link, out var toNode);
                var fetchedBack = peer.Fetch(back, out var toPeer);
                var applied = toNode is null ? fetched : node.Redeliver(link, toNode);
                var appliedBack = toPeer is null ? fetchedBack : peer.Redeliver(back, toPeer);
                return $"{node} ⇄ {peer} crossed: {applied}/{appliedBack}";
            }
            case SimStepKind.ChildrenLocal:
            {
                var offered = rows.Where(r => SimKinds.Of(r.Kind).Codec.Descriptor.SupportsChildrenLocal).ToList();
                if (offered.Count == 0) return "nothing offers 'sync the definition only'";
                var row = offered[s.B % offered.Count];
                node.SetChildrenLocal(row, !row.ChildrenLocal);
                return $"{node} turns 'sync the definition only' {(row.ChildrenLocal ? "on" : "off")} for {row.Kind}:{row.LocalKey}";
            }
            case SimStepKind.SyncAll:
                SyncRound();
                return "every link pulls once";
            case SimStepKind.Resolve:
            {
                var desktops = Desktops.ToList();
                var desktop = desktops[s.A % desktops.Count];
                var items = desktop.OpenItems.OrderBy(i => i.Id).ToList();
                if (items.Count == 0 || !desktop.Verified) return $"{desktop} has nothing to resolve";
                var item = items[s.B % items.Count];
                var allowed = desktop.AllowedActions(item);
                var action = allowed[s.C % allowed.Count];
                var (custom, target) = Inputs(item, action, s.D);
                var done = desktop.Resolve(item, action, custom, target);
                Resolutions++;
                return $"{desktop} resolves {item} with {action}{(done ? "" : " (stale: closed)")}";
            }
            case SimStepKind.Use:
            {
                if (Row(s.B) is not { } row) return "nothing to use";
                var ids = SimKinds.Of(row.Kind).UsableChildIds(row.Content!);
                if (ids.Count == 0) return "no usable child";
                var id = ids[s.C % ids.Count];
                node.UseChild(row, id, s.D % 3);
                return $"{node} uses {row.Kind}:{row.LocalKey}/{id} × {s.D % 3}";
            }
            case SimStepKind.Values:
            {
                if (Row(s.B) is not { } row) return "no row";
                node.SetRowValues(row, s.C % 3);
                return $"{node} {row.Kind}:{row.LocalKey} has {s.C % 3} values";
            }
            case SimStepKind.Reorder:
            {
                var ordered = rows.Where(r => SimKinds.Of(r.Kind).HasOrder).ToList();
                if (ordered.Count < 2) return "nothing to reorder";
                var row = ordered[s.B % ordered.Count];
                node.Move(row, s.C % ordered.Count);
                return $"{node} moves {row.Kind}:{row.LocalKey} to {s.C % ordered.Count}";
            }
            case SimStepKind.EntityState:
            {
                if (Row(s.B) is not { } row || !node.Verified) return "no row";
                var state = (s.C % 3) switch
                {
                    0 => DataSyncEntitySyncState.LocalOnly,
                    1 => DataSyncEntitySyncState.Detached,
                    _ => DataSyncEntitySyncState.Synced,
                };
                if (state != DataSyncEntitySyncState.Synced && row.State != DataSyncEntitySyncState.Synced)
                    state = DataSyncEntitySyncState.Synced;
                node.SetEntitySync(row, state);
                return $"{node} sets {row.Kind}:{row.LocalKey} {state}";
            }
            case SimStepKind.Undo:
            {
                var entries = node.Db.History.Where(h => !h.Undone && h.Kind != DataSyncHistoryKind.Undo &&
                                                         h.Kind != DataSyncHistoryKind.Restore).ToList();
                if (entries.Count == 0 || !node.Verified) return "nothing to undo";
                var entry = entries[entries.Count - 1 - s.B % Math.Min(entries.Count, 4)];
                var refusals = node.Undo(entry);
                return $"{node} undoes #{entry.Id} {entry.Kind} ({string.Join(",", entry.Changes.Select(c => c.Action))})" +
                       (refusals.Count > 0 ? $" refused [{string.Join(",", refusals)}]" : "");
            }
            case SimStepKind.Backup:
            {
                if (!_backups.TryGetValue(node.NodeId, out var list)) _backups[node.NodeId] = list = [];
                list.Add(node.Backup());
                if (list.Count > 3) list.RemoveAt(0);
                return $"{node} is backed up";
            }
            case SimStepKind.RestoreDatabase or SimStepKind.RestoreDirectory:
            {
                if (!_backups.TryGetValue(node.NodeId, out var list) || list.Count == 0) return "no backup";
                var backup = list[s.B % list.Count];
                if (s.Kind == SimStepKind.RestoreDatabase) node.RestoreDatabase(backup);
                else node.RestoreDirectory(backup);
                var edits = new List<string>();
                var random = new Random(s.D);
                for (var i = s.C % 3; i > 0; i--)
                {
                    var live = node.Rows.Where(r => r.IsLive).ToList();
                    if (live.Count == 0) break;
                    var row = live[random.Next(live.Count)];
                    if (SimKinds.Of(row.Kind).RandomEdit(random, row.Content!, World.NewChildId) is { } edit)
                    {
                        node.EditRow(row, edit.Content);
                        edits.Add(edit.What);
                    }
                }

                return $"{node}'s {(s.Kind == SimStepKind.RestoreDatabase ? "database" : "data directory")} is restored to " +
                       $"{backup.At:MM-dd HH:mm}; edits before its first pull: [{string.Join(",", edits)}]";
            }
            case SimStepKind.Restart:
                node.Restart();
                return $"{node} restarts";
            case SimStepKind.StopLink:
                if (Link(s.B) is not { } stop || stop.Stopped) return "nothing to stop";
                node.StopLink(stop);
                return $"{node} stops {stop}";
            case SimStepKind.StartLink:
            {
                var stopped = links.Where(l => l.Stopped).ToList();
                if (stopped.Count == 0) return "nothing to start";
                var link = stopped[s.B % stopped.Count];
                node.StartLink(link);
                return $"{node} starts {link}";
            }
            case SimStepKind.ResetLink:
                if (Link(s.B) is not { } reset) return "nothing to reset";
                var fresh = node.ResetLink(reset);
                return $"{node} resets {reset} → {fresh}";
            case SimStepKind.Partition:
            {
                var cut = new List<string>();
                var bit = 0;
                foreach (var reader in Nodes)
                foreach (var source in reader.Links.Values.Select(l => l.Peer))
                {
                    if ((s.B >> (bit++ % 20) & 1) == 0) continue;
                    World.Down.Add((reader.NodeId, source.NodeId));
                    cut.Add($"{reader}←{source}");
                }

                return $"partition [{string.Join(",", cut)}]";
            }
            case SimStepKind.Heal:
                World.Down.Clear();
                return "heal";
            case SimStepKind.LongPartition:
            {
                foreach (var other in Nodes.Where(n => n != node))
                {
                    World.Down.Add((node.NodeId, other.NodeId));
                    World.Down.Add((other.NodeId, node.NodeId));
                }

                World.Clock.Advance(TimeSpan.FromDays(181));
                foreach (var n in Nodes)
                {
                    n.RunRetention();
                    n.UpdateVerified();
                }

                return $"{node} is cut off for 181 days (retention runs)";
            }
            case SimStepKind.Advance:
            {
                var minutes = s.B % (3 * 24 * 60);
                World.Clock.Advance(TimeSpan.FromMinutes(minutes));
                foreach (var n in Nodes) n.RunRetention();
                return $"{minutes} minutes pass";
            }
            case SimStepKind.StaleWrite:
            {
                var recent = rows.Where(r => r.PreApplyContent is not null && r.LastApply is { } last &&
                                             DataSyncLostUpdateGuard.InWindow(last.At, World.Clock.Now)).ToList();
                if (recent.Count == 0) return "no recent apply to overwrite";
                var row = recent[s.B % recent.Count];
                node.EditRow(row, row.PreApplyContent!);
                return $"{node}: a whole-row writer puts back {row.Kind}:{row.LocalKey} as it read it before the apply";
            }
            case SimStepKind.SyncStaleWrite:
            {
                if (Link(s.B) is not { } link) return "no link";
                var outcome = node.Pull(link);
                var recent = node.Rows.Where(r => r.IsLive && r.PreApplyContent is not null && r.LastApply?.At == World.Clock.Now)
                    .OrderBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.LocalKey, StringComparer.Ordinal).ToList();
                if (recent.Count == 0) return $"{node} ← {link.Peer}: {outcome}, nothing applied to write back";
                var row = recent[s.C % recent.Count];
                node.EditRow(row, row.PreApplyContent!);
                return $"{node} ← {link.Peer}: {outcome}; a whole-row writer that read {row.Kind}:{row.LocalKey} before " +
                       "the apply writes it back";
            }
            case SimStepKind.Bulk:
            {
                var random = new Random(s.D);
                switch (s.C % 3)
                {
                    case 0:
                        for (var i = 0; i < 51; i++)
                        {
                            node.Create(ExtensionGroupSimKind.Instance.Kind, new Bakabase.Modules.DataSync.Kinds.ExtensionGroups.ExtensionGroupContentV1(
                                "Set " + World.NewChildId(), [ExtensionGroupSimKind.Pool[random.Next(ExtensionGroupSimKind.Pool.Length)]]));
                        }

                        return $"{node} creates 51 extension groups at once";
                    case 1:
                        foreach (var row in rows.Take(51)) node.EditRow(row, SimKinds.Of(row.Kind).Renamed(row.Content!, row.Name + "+"));
                        return $"{node} renames {Math.Min(51, rows.Count)} definitions at once";
                    default:
                        foreach (var row in rows.Take(12)) node.DeleteRow(row);
                        return $"{node} deletes {Math.Min(12, rows.Count)} definitions at once";
                }
            }
            case SimStepKind.RestoreOffline:
            {
                // The residual window of §5.6: a whole directory restored while every peer is away; after two minutes
                // the actor is verified by the timeout, and local edits reissue counters the peers have already seen.
                if (!_backups.TryGetValue(node.NodeId, out var list) || list.Count == 0) return "no backup";
                foreach (var other in Nodes.Where(n => n != node))
                {
                    World.Down.Add((node.NodeId, other.NodeId));
                    World.Down.Add((other.NodeId, node.NodeId));
                }

                node.RestoreDirectory(list[0]);
                World.Clock.Advance(SimNode.UnverifiedTimeout + TimeSpan.FromMinutes(1));
                node.UpdateVerified();
                var random = new Random(s.D);
                var edits = new List<string>();
                for (var i = 1 + s.C % 3; i > 0; i--)
                {
                    var live = node.Rows.Where(r => r.IsLive).ToList();
                    if (live.Count == 0) break;
                    var row = live[random.Next(live.Count)];
                    if (SimKinds.Of(row.Kind).RandomEdit(random, row.Content!, World.NewChildId) is { } edit)
                    {
                        node.EditRow(row, edit.Content);
                        edits.Add(edit.What);
                    }
                }

                node.Refresh();
                ResidualRestore = true;
                return $"{node}'s data directory is restored to {list[0].At:MM-dd HH:mm} while it is cut off; edits " +
                       $"[{string.Join(",", edits)}] reissue counters";
            }
            case SimStepKind.Include:
            {
                var excluded = links.SelectMany(l => l.Bases.Where(b => b.Value.State == DataSyncBaseState.Excluded)
                    .Select(b => (Link: l, b.Key))).ToList();
                if (excluded.Count == 0) return "nothing excluded";
                var (link, key) = excluded[s.B % excluded.Count];
                node.Include(link, key);
                return $"{node} includes {key.Kind}/{key.Key.Value[..6]} again on {link}";
            }
            case SimStepKind.UndoRelink:
            {
                // §8.11: undo a definition sync created here, then link to that peer anew (a reset link has no base
                // for it, like any link made after the undo). The undone create must stay excluded there too, until
                // the person includes it on that link.
                var entries = node.Db.History
                    .Where(h => !h.Undone && h.Kind is not (DataSyncHistoryKind.Undo or DataSyncHistoryKind.Restore) &&
                                h.Changes.Any(c => c.Action == "created" && c.LinkId is not null))
                    .ToList();
                if (entries.Count == 0 || !node.Verified) return "no create to undo";
                var entry = entries[entries.Count - 1 - s.B % Math.Min(entries.Count, 4)];
                var refusals = node.Undo(entry);
                var linkId = entry.Changes.First(c => c.Action == "created" && c.LinkId is not null).LinkId;
                if (node.LinkById(linkId) is not { } old) return $"no link left to reset after undoing #{entry.Id}";
                var relinked = node.ResetLink(old);
                var outcome = node.Pull(relinked);
                var excluded = relinked.Bases.Values.Count(b => b.Exclusion == DataSyncExclusionReason.Undone);
                if (excluded > 0) World.Count("undoRelink:excluded");
                var refused = refusals.Count > 0 ? $" (refused [{string.Join(",", refusals)}])" : "";
                return $"{node} undoes #{entry.Id}{refused}, resets {old} → {relinked} and pulls: {outcome}, " +
                       $"{excluded} undone create(s) excluded";
            }
            case SimStepKind.EditApplied:
                return EditApplied(s);
            default:
                throw new ArgumentOutOfRangeException(nameof(s), s.Kind, null);
        }
    }

    /// <summary>
    /// §8.4 row K6: a conflicted merge applied the record's other paths and waits on the rest. The person changes one
    /// path the last apply changed (back to what it was), after the lost-update window, while the conflict is open;
    /// the record merged again (condition 2, as the next pull would) applies nothing — its applied base says it has
    /// already — so the edit stands. Merged against the old base instead, it was taken again and published.
    /// </summary>
    private string EditApplied(SimStep s)
    {
        // Conflicts are rare, so the step takes the first node from its own that has one open.
        List<(SimLink Link, DataSyncPeerBase Base, SimRow? Row)> open = [];
        var node = Nodes[s.A % Nodes.Count];
        for (var i = 0; i < Nodes.Count && open.Count == 0; i++)
        {
            node = Nodes[(s.A + i) % Nodes.Count];
            if (!node.Verified) continue;
            var rows = node.Rows;
            open = node.Links.Values.Where(l => l.Paused is null && !l.Stopped).OrderBy(l => l.Id)
                .SelectMany(l => l.Bases.Values
                    .Where(b => b.Pending is { Reason: DataSyncPendingReason.Conflict, AppliedBase: not null })
                    .OrderBy(b => b.Kind, StringComparer.Ordinal).ThenBy(b => b.Key.Value, StringComparer.Ordinal)
                    .Select(b => (Link: l, Base: b)))
                .Select(x => (x.Link, x.Base, Row: rows.FirstOrDefault(r => r.IsLive && r.Kind == x.Base.Kind &&
                                                                        r.Keys.Contains(x.Base.Key))))
                .Where(x => x.Row is { LastApply: not null, PublishHeld: false, State: DataSyncEntitySyncState.Synced })
                .ToList();
        }

        if (open.Count == 0) return "no open conflict with applied changes";
        var (link, b, row) = open[s.B % open.Count];
        var kind = SimKinds.Of(row!.Kind);
        var singles = Singles(row.LastApply!.Value.Changes);
        if (singles.Count == 0) return "no applied change to edit";
        var one = singles[s.C % singles.Count];
        if (kind.Revert(row.Content!, one, out _) is not { } reverted) return "no longer the applied value";

        World.Clock.Advance(DataSyncLostUpdateGuard.Window + TimeSpan.FromMinutes(1));
        foreach (var n in Nodes) n.UpdateVerified();
        node.EditRow(row, reverted);
        var edited = row.Content;
        var outcome = node.Remerge(link, [(b.Kind, b.Key)]);
        if (outcome == SimPullOutcome.Applied && row.IsLive && !row.PublishHeld)
        {
            if (!Equals(row.Content, edited))
            {
                Failures.Add($"K6: {node} re-merged the open conflict of {row.Name} on {link} and undid an edit made " +
                             $"since: {edited} became {row.Content}");
            }
            else
            {
                World.Count("editApplied:kept");
            }
        }

        return $"{node} changes an applied path of {row.Kind}:{row.LocalKey} back while its conflict is open, " +
               $"re-merges on {link}: {outcome}";
    }

    /// <summary>Each change of <paramref name="changes"/> as a list of its own.</summary>
    private static List<DataSyncEntityChangeList> Singles(DataSyncEntityChangeList changes)
    {
        var none = DataSyncEntityChangeList.Empty;
        return changes.Scalars.Select(c => none with { Scalars = [c] })
            .Concat(changes.Added.Select(c => none with { Added = [c] }))
            .Concat(changes.Removed.Select(c => none with { Removed = [c] }))
            .Concat(changes.Renamed.Select(c => none with { Renamed = [c] }))
            .ToList();
    }

    /// <summary>The custom name/label and the target a random resolution passes.</summary>
    private static (string? Custom, string? Target) Inputs(SimItem item, DataSyncInboxAction action, int arg)
    {
        switch (action)
        {
            case DataSyncInboxAction.UseCustom:
                return (item.Subject == "name" ? "Custom" + arg % 3 : TestItemSimKind.Labels[arg % TestItemSimKind.Labels.Length], null);
            case DataSyncInboxAction.Link or DataSyncInboxAction.KeepWithEntity:
            {
                var candidates = item.Payload.Candidates!.Where(c => c.Updatable).ToList();
                return (null, candidates[arg % candidates.Count].LocalKey);
            }
            case DataSyncInboxAction.KeepRecordLinked:
                return (null, item.Payload.Records![arg % item.Payload.Records.Count].PrimaryKey);
            case DataSyncInboxAction.KeepBoth:
                return (null, null);
            default:
                return (null, null);
        }
    }

    /// <summary>Every link of every node pulls once, in a fixed order.</summary>
    private void SyncRound()
    {
        foreach (var node in Nodes)
        foreach (var link in node.Links.Values.OrderBy(l => l.Id).ToList())
            node.Pull(link);
    }

    // ---- quiescence ------------------------------------------------------------------------------------

    private void Quiesce()
    {
        Steps.Add("-- quiescence --");
        World.Down.Clear();
        World.Clock.Advance(TimeSpan.FromHours(25));
        foreach (var node in Nodes)
        {
            node.UpdateVerified();
            foreach (var link in node.Links.Values.Where(l => l.Stopped).ToList()) node.StartLink(link);
        }

        // §13.3's quiescence decides first; the rounds before it only let every question reach its desktop. Merges
        // may keep revising while a question waits (a type kept on one device and changed on another, relayed by a
        // third), so the chooser starts when decisions are open; a world that never settles with nothing left to
        // decide is a failure.
        if (Stabilize("the first rounds", failOnChurn: false)) CheckAttention();
        else if (Nodes.All(n => !n.OpenItems.Any()))
        {
            Failures.Add($"quiescence (the first rounds): the nodes never stopped changing after {MaxRounds} rounds " +
                         "with nothing left to decide (a ping-pong)");
            return;
        }
        else
        {
            World.Count("quiescence:churnWhileDecisionsWait");
        }

        // Twice: the decisions, then a day later — every link's daily full reconciliation (§8.8) re-reads what an
        // incremental pull can no longer show, such as a tombstone read while its entity was unknown here (row N1)
        // that a restored device's reissued Seqs made its peers take for already delivered. It may raise questions
        // of its own, which the chooser answers too.
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                Steps.Add("-- a day later --");
                World.Clock.Advance(TimeSpan.FromHours(25));
                foreach (var node in Nodes) node.UpdateVerified();
                if (!Stabilize("the daily reconciliation", failOnChurn: false) && Nodes.All(n => !n.OpenItems.Any()) &&
                    !Stabilize("the daily reconciliation, nothing to decide")) return;
            }

            var asked = new Dictionary<(string, DataSyncInboxItemType, string, SyncKey, string), int>();
            for (var i = 0; ; i++)
            {
                if (i == MaxChooserSteps)
                {
                    Failures.Add("quiescence: the chooser never ran out of decisions (decisions livelock)");
                    return;
                }

                var (node, item) = NextDecision();
                if (item is null) break;
                var question = (node!.NodeId, item.Type, item.Kind, item.Key, item.Subject);
                if ((asked[question] = asked.GetValueOrDefault(question) + 1) > MaxDecisionsPerQuestion)
                {
                    Failures.Add($"quiescence: {node} was asked {item.Type} about {item.Kind}/{item.Key.Value[..6]} '{item.Subject}' " +
                                 $"{MaxDecisionsPerQuestion} times (a decisions livelock)");
                    return;
                }

                World.Count(node!.Headless ? "chooser:headless" : "chooser:desktop");
                if (pass == 1) World.Count("chooser:afterDailyReconciliation");
                var (action, target) = Decide(node!, item);
                node!.Resolve(item, action, null, target);
                Resolutions++;
                Steps.Add($"chooser: {node} resolves {item} with {action}");
                // As before the first decision: merges may keep revising while other questions wait (a type kept on
                // one device and converted on another); only churn with nothing left to decide is a ping-pong.
                if (!Stabilize("after a decision", failOnChurn: false))
                {
                    if (Nodes.All(n => !n.OpenItems.Any()) && !Stabilize("after the last decision")) return;
                    World.Count("quiescence:churnWhileDecisionsWait");
                }

                if (Environment.GetEnvironmentVariable("DATASYNC_FUZZ_VERBOSE") is { Length: > 0 } && i < 12)
                    Steps.Add(string.Concat(Nodes.Select(SimDigest.Describe)));
            }

            if (!Stabilize("the final rounds")) return;
        }

        // I2: one more full round creates zero revisions and zero Seq bumps.
        var counters = Nodes.Select(n => (n.ActorCounter, n.LastSeq, n.Actor)).ToList();
        var digests = Nodes.Select(SimDigest.Of).ToList();
        SyncRound();
        for (var i = 0; i < Nodes.Count; i++)
        {
            if ((Nodes[i].ActorCounter, Nodes[i].LastSeq, Nodes[i].Actor) != counters[i])
                Failures.Add($"I2: one more round changed {Nodes[i]}:\n{Diff(digests[i], SimDigest.Of(Nodes[i]))}");
        }

        CheckEquality();
        CheckVectors();
        CheckInbox();
        CollectViolations("quiescence");
    }

    /// <summary>Rounds (with resumes and restore choices) until the whole world's state stops changing.</summary>
    private bool Stabilize(string phase, bool failOnChurn = true)
    {
        var before = SimDigest.Of(World);
        var previous = before;
        for (var round = 0; round < MaxRounds; round++)
        {
            previous = before;
            foreach (var node in Nodes)
            {
                if (node.RestorePending && node.Verified)
                {
                    // OthersWin merges one cycle with the Follow rule; on a pull cycle of three or more devices that
                    // meets the same rotation limit as a configured Follow (§8.1), so those shapes choose the other.
                    var choice = _spec.Seed % 2 == 0 || OnPullCycle(node)
                        ? DataSyncRestoreChoice.ThisDeviceWins
                        : DataSyncRestoreChoice.OthersWin;
                    node.ChooseRestore(choice);
                    Steps.Add($"quiescence: {node} chooses {choice}");
                }

                foreach (var link in node.Links.Values.Where(l => l.Paused is not null).ToList())
                {
                    // B7's guidance: the device whose actor was duplicated resets its identity — this one ("reset
                    // identity"), or the other ("On {name}, open Data sync").
                    if (link.Paused == DataSyncPauseReason.PeerIdentityDuplicated &&
                        Nodes.FirstOrDefault(n => link.PausedDetail?.Contains("actor=" + n.Actor.Value, StringComparison.Ordinal) == true)
                            is { } duplicated)
                    {
                        duplicated.ResetIdentity();
                        Steps.Add($"quiescence: {duplicated} resets its identity (its actor was duplicated)");
                    }

                    node.Resume(link);
                }
            }

            SyncRound();
            CheckKeyInvariant(phase);
            if (Environment.GetEnvironmentVariable("DATASYNC_FUZZ_VERBOSE") == "rounds")
                Steps.Add($"after round {round} of {phase}:\n" + string.Concat(Nodes.Select(SimDigest.Describe)));
            var after = SimDigest.Of(World);
            if (after == before) return true;
            before = after;
        }

        if (failOnChurn)
        {
            Failures.Add($"quiescence ({phase}): the nodes never stopped changing after {MaxRounds} rounds (a ping-pong); " +
                         $"the last round changed:\n{Diff(previous, before)}");
        }

        return false;
    }

    /// <summary>The next open item: on a desktop first; on a headless node only when no desktop has one left.</summary>
    private (SimNode? Node, SimItem? Item) NextDecision()
    {
        foreach (var node in Desktops)
        {
            if (node.OpenItems.OrderBy(i => i.Id).FirstOrDefault() is { } item) return (node, item);
        }

        foreach (var node in Nodes.Where(n => n.Headless))
        {
            if (node.OpenItems.OrderBy(i => i.Id).FirstOrDefault() is { } item)
            {
                HubFallbackResolutions++;
                return (node, item);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// The deterministic chooser of quiescence: an action that settles the question for good. A name match links
    /// only to a candidate created here and never linked (two devices created it separately); any other candidate
    /// keeps both, since linking it would merge two lineages another device keeps apart and only raise an
    /// identity question there, which a chooser answering the same way on every device would never finish.
    /// </summary>
    public static (DataSyncInboxAction Action, string? Target) Decide(SimNode node, SimItem item)
    {
        var allowed = node.AllowedActions(item);
        switch (item.Type)
        {
            case DataSyncInboxItemType.FieldConflict or DataSyncInboxItemType.ChildRenameConflict:
                return (DataSyncInboxAction.KeepLocal, null);
            case DataSyncInboxItemType.TypeChange:
                return (DataSyncInboxAction.Convert, null);
            case DataSyncInboxItemType.DeletedThere or DataSyncInboxItemType.ChildDeletedInUse:
                return (DataSyncInboxAction.DeleteHere, null);
            case DataSyncInboxItemType.DeletedHereEditedThere:
                return (DataSyncInboxAction.RestoreHere, null);
            case DataSyncInboxItemType.LinkSuggestion:
            {
                var fresh = item.Payload.Candidates!.Where(c => c.Updatable).FirstOrDefault(c =>
                    node.LiveRow(item.Kind, c.LocalKey) is { Keys.Count: 1 } row && row.Origin == node.NodeId);
                return fresh is not null && allowed.Contains(DataSyncInboxAction.Link)
                    ? (DataSyncInboxAction.Link, fresh.LocalKey)
                    : (DataSyncInboxAction.KeepBoth, null);
            }
            case DataSyncInboxItemType.IdentityConflict when allowed.Contains(DataSyncInboxAction.KeepWithEntity):
            {
                // The record stays with the entity that holds its primary key (its own lineage); the others give
                // up its aliases. Every device answering this way moves keys towards the same owners.
                // A candidate deleted since the item was derived is left out; with none left the answer is stale and
                // Resolve re-derives the question (§9.2).
                var candidates = item.Payload.Candidates!.Where(c => c.Updatable)
                    .Select(c => node.LiveRow(item.Kind, c.LocalKey)).OfType<SimRow>().ToList();
                var chosen = candidates.FirstOrDefault(r => r.Keys.Contains(item.Key)) ??
                             candidates.OrderBy(r => r.Primary.Value, StringComparer.Ordinal).FirstOrDefault();
                return (DataSyncInboxAction.KeepWithEntity,
                    chosen?.LocalKey ?? item.Payload.Candidates!.First(c => c.Updatable).LocalKey);
            }
            case DataSyncInboxItemType.IdentityConflict:
                return allowed.Contains(DataSyncInboxAction.KeepRecordLinked)
                    ? (DataSyncInboxAction.KeepRecordLinked, item.Payload.Records!
                        .OrderBy(r => r.PrimaryKey == node.RowOf(item.Kind, item.Key).Primary.Value ? 0 : 1)
                        .ThenBy(r => r.PrimaryKey, StringComparer.Ordinal).First().PrimaryKey)
                    : (DataSyncInboxAction.Detach, null);
            case DataSyncInboxItemType.MassChildDeletion or DataSyncInboxItemType.LargeChange:
                return (DataSyncInboxAction.ApplyAll, null);
            case DataSyncInboxItemType.SuspectedLostUpdate:
                return (DataSyncInboxAction.Publish, null);
            default:
                return (allowed[0], null);
        }
    }

    // ---- invariants ------------------------------------------------------------------------------------

    /// <summary>I8: within a kind, every key names exactly one row (live or tombstone) on each node.</summary>
    private void CheckKeyInvariant(string when)
    {
        foreach (var node in Nodes)
        {
            foreach (var group in node.Rows.SelectMany(r => r.Keys.Select(k => (r.Kind, Key: k, Row: r)))
                         .GroupBy(x => (x.Kind, x.Key)).Where(g => g.Count() > 1))
                Failures.Add($"I8 after {when}: {node} has key {group.Key.Key.Value[..8]} on {group.Count()} rows");
        }
    }

    private void CollectViolations(string when)
    {
        foreach (var node in Nodes)
        {
            foreach (var violation in node.Violations) Failures.Add($"{violation} (at {when})");
            node.Violations.Clear();
        }
    }

    /// <summary>I9: every open item on a headless node is counted in the attention each desktop reading it shows.</summary>
    private void CheckAttention()
    {
        foreach (var hub in Nodes.Where(n => n.Headless))
        foreach (var desktop in Desktops)
        {
            if (desktop.Links.GetValueOrDefault(hub.NodeId) is not { } link || link.Stopped) continue;
            if (link.PeerAttention?.OpenDecisions != hub.OpenDecisions)
                Failures.Add($"I9: {desktop} shows {link.PeerAttention?.OpenDecisions} decisions waiting on {hub}, which has {hub.OpenDecisions}");
        }
    }

    /// <summary>Groups of nodes that pull each other both ways (the equality of I1 holds within each).</summary>
    private List<List<SimNode>> Components()
    {
        var parent = Nodes.ToDictionary(n => n, n => n);
        SimNode Find(SimNode n) => parent[n] == n ? n : parent[n] = Find(parent[n]);
        foreach (var a in Nodes)
        foreach (var link in a.Links.Values.Where(l => !l.Stopped))
        {
            if (link.Peer.Links.GetValueOrDefault(a.NodeId) is { Stopped: false }) parent[Find(a)] = Find(link.Peer);
        }

        return Nodes.GroupBy(Find).Select(g => g.ToList()).Where(g => g.Count > 1).ToList();
    }

    /// <summary>
    /// I1 (and I10): within each component, an entity synced on every node has one comparison form everywhere;
    /// an entity synced live on one node is live on all, unless a node keeps it out of sync (local-only,
    /// detached, or an exclusion on its link).
    /// </summary>
    private void CheckEquality()
    {
        foreach (var component in Components())
        foreach (var kind in SimKinds.All)
        {
            var rows = component.SelectMany(n => n.Rows.Where(r => r.Kind == kind.Kind && r.HasSideRow).Select(r => (Node: n, Row: r)))
                .ToList();
            foreach (var group in GroupByKeys(rows))
            {
                var keys = group.SelectMany(x => x.Row.Keys).ToHashSet();
                if (group.Any(x => x.Row.IsLive && x.Row.State != DataSyncEntitySyncState.Synced)) continue;
                if (group.Any(x => x.Row.Deleted && x.Row.TombstoneKind == DataSyncTombstoneKind.UndoneCreate)) continue;
                if (component.Any(n => n.Links.Values.Any(l => l.Bases.Values.Any(b => b.State == DataSyncBaseState.Excluded &&
                        b.Kind == kind.Kind && (keys.Contains(b.Key) || b.ExclusionKeys.Any(e => keys.Contains(new SyncKey(e)))))))) continue;

                // §5.6's residual windows accept that a restored device reissued counters its peers already held: an
                // entity whose history names such an actor may end apart after the pause row A2 raises (the
                // documented outcome is the pause, not equality).
                if (Nodes.SelectMany(n => n.ResidualActors).ToHashSet(StringComparer.Ordinal) is { Count: > 0 } reissued &&
                    group.Any(x => x.Row.Vv.Counters.Keys.Any(reissued.Contains)))
                {
                    World.Count("residualWindow:equalityNotRequired");
                    continue;
                }

                var live = group.Where(x => x.Row.IsLive).ToList();
                var name = live.Select(x => x.Row.Name).FirstOrDefault() ?? keys.First().Value[..8];
                foreach (var node in component)
                {
                    var mine = group.Where(x => x.Node == node).ToList();
                    if (mine.Count > 1 && mine.Count(x => x.Row.IsLive) > 1)
                        Failures.Add($"I1: {node} holds {name} as {mine.Count} entities");
                    if (live.Count > 0 && mine.All(x => !x.Row.IsLive))
                        Failures.Add($"I1: {name} is live on {string.Join(",", live.Select(x => x.Node).Distinct())} " +
                                     $"but {(mine.Count == 0 ? "unknown" : "deleted")} on {node}");
                }

                var forms = live.Select(x => (x.Node, Form: FormOf(x.Row))).DistinctBy(x => x.Form).ToList();
                if (forms.Count > 1)
                {
                    Failures.Add($"I1: {name} differs: " + string.Join(" | ",
                        live.Select(x => $"{x.Node} {x.Row.Content} ok {x.Row.OrderKey} vv {x.Row.Vv}")));
                }
            }
        }
    }

    private static IEnumerable<List<(SimNode Node, SimRow Row)>> GroupByKeys(List<(SimNode Node, SimRow Row)> rows)
    {
        var parent = Enumerable.Range(0, rows.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        var byKey = new Dictionary<SyncKey, int>();
        for (var i = 0; i < rows.Count; i++)
        {
            foreach (var key in rows[i].Row.Keys)
            {
                if (byKey.TryGetValue(key, out var j)) parent[Find(i)] = Find(j);
                else byKey[key] = i;
            }
        }

        return Enumerable.Range(0, rows.Count).GroupBy(Find).Select(g => g.Select(i => rows[i]).ToList());
    }

    private static string FormOf(SimRow row) =>
        DataSyncPublication.Of(SimKinds.Of(row.Kind).Codec, row.Content!, row.Overlay, row.ChildrenLocal, row.OrderKey,
            row.Unknown).SharedHash ?? "held";

    /// <summary>
    /// I7: every live entity's vector covers every base vector for it; two different comparison forms never share
    /// a vector (except in the residual restore window, which must have paused instead of merging).
    /// </summary>
    private void CheckVectors()
    {
        foreach (var node in Nodes)
        foreach (var row in node.Rows.Where(r => r.IsLive && r.HasSideRow && r.State == DataSyncEntitySyncState.Synced))
        foreach (var link in node.Links.Values)
        {
            if (link.Bases.GetValueOrDefault((row.Kind, row.Primary)) is { State: DataSyncBaseState.Normal, Vv: { } baseVv } &&
                row.Vv.CompareTo(baseVv) is not (DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates))
                Failures.Add($"I7: {node} {row.Name} vv {row.Vv} does not cover its base on {link} ({baseVv})");
        }

        if (ResidualRestore || Nodes.Any(n => n.ResidualWindow)) return;
        foreach (var kind in SimKinds.All)
        {
            var byVector = Nodes.SelectMany(n => n.Rows.Where(r => r.Kind == kind.Kind && r.IsLive && r.HasSideRow &&
                                                                   r.State == DataSyncEntitySyncState.Synced)
                    .Select(r => (Node: n, Row: r)))
                .GroupBy(x => x.Row.Vv.ToCanonicalString());
            foreach (var group in byVector)
            {
                if (group.Select(x => FormOf(x.Row)).Distinct().Count() > 1)
                    Failures.Add($"I7: vector {group.Key} carries different forms: " +
                                 string.Join(" | ", group.Select(x => $"{x.Node} {x.Row.Content}")));
            }
        }
    }

    /// <summary>I5: after quiescence nothing waits anywhere, and a headless node never notified anyone.</summary>
    private void CheckInbox()
    {
        foreach (var node in Nodes)
        {
            foreach (var item in node.OpenItems) Failures.Add($"I5: {node} still has {item} open after quiescence");
            if (node.Headless && node.Notifications.Count > 0)
                Failures.Add($"I5: headless {node} created {node.Notifications.Count} notifications");
            foreach (var link in node.Links.Values.Where(l => l.Paused is not null))
                Failures.Add($"quiescence: {node}'s {link} is still paused");
        }
    }

    // ---- reporting -------------------------------------------------------------------------------------

    public string Report()
    {
        var text = new StringBuilder();
        text.AppendLine($"seed {_spec.Seed}: {Failures.Count} failure(s)");
        foreach (var failure in Failures.Take(8)) text.AppendLine("  " + failure);
        text.AppendLine("steps:");
        foreach (var step in Steps) text.AppendLine("  " + step);
        text.AppendLine("final states:");
        foreach (var node in Nodes) text.Append(SimDigest.Describe(node));
        var lines = int.TryParse(Environment.GetEnvironmentVariable("DATASYNC_FUZZ_LOG"), out var n) ? n : 60;
        text.AppendLine($"engine log (last {lines}):");
        foreach (var line in World.Trace.TakeLast(lines)) text.AppendLine("  " + line);
        return text.ToString();
    }

    private static string Diff(string before, string after)
    {
        var a = before.Split('\n');
        var b = after.Split('\n');
        return string.Join("\n", a.Except(b).Select(l => "  - " + l).Concat(b.Except(a).Select(l => "  + " + l)).Take(20));
    }

    // ---- shrinking -------------------------------------------------------------------------------------

    /// <summary>Runs a scenario; its failures, or none.</summary>
    public static SimScenario Execute(SimScenarioSpec spec)
    {
        var scenario = new SimScenario(spec);
        scenario.Run();
        return scenario;
    }

    /// <summary>
    /// A smaller scenario that still fails: steps are removed greedily (halves, then one at a time) while any
    /// failure remains. Bounded, so a failing seed still reports quickly.
    /// </summary>
    public static SimScenarioSpec Shrink(SimScenarioSpec spec, int budget = 400)
    {
        var steps = spec.Steps.ToList();
        var runs = 0;
        bool Fails(List<SimStep> candidate)
        {
            runs++;
            return Execute(spec with { Steps = candidate }).Failures.Count > 0;
        }

        for (var chunk = steps.Count / 2; chunk >= 1 && runs < budget; chunk /= 2)
        {
            for (var start = 0; start + chunk <= steps.Count && runs < budget;)
            {
                var candidate = steps.Take(start).Concat(steps.Skip(start + chunk)).ToList();
                if (Fails(candidate)) steps = candidate;
                else start += chunk;
            }
        }

        return spec with { Steps = steps };
    }
}
