using System.Globalization;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.DataSync.Convergence;

/// <summary>
/// The convergence property test (§13.3) on the real stack: seeded random scenarios over 2–4 hosts (a pair, a chain,
/// a star around a headless hub, a mesh; two-way, Follow, mutual Follow) whose steps are what people and the hosts'
/// loops do — create, rename, add, rename, recolour and remove options, toggle IgnoreCase with case duplicates, edit
/// tag groups, move multilevel nodes, change types, delete, hold values, detach and keep on one device, pull, decide
/// what an item asks, undo, restore a database, stop, start and reset links, cut hosts off and advance the clock.
/// After every step: no key names two rows (I8) and no option or definition resources use went away without a person
/// (I4). Then quiescence — healed, links resumed, restore choices made, every item decided by a chooser on a desktop,
/// rounds until nothing changes, and again a day later (and the day after, when a link completed its first contact)
/// — and the invariants of the end: another round changes nothing (I2), hosts that pull each other hold the same
/// forms and children (I1, I10), nothing waits and the hub notified nobody (I5). CI runs the seeds below;
/// <c>DATASYNC_CONVERGENCE_SEED</c> sets the first and <c>DATASYNC_CONVERGENCE_RUNS</c> how many (for longer local
/// runs). A failing seed prints its steps and hosts.
/// </summary>
[TestClass]
public class DataSyncConvergenceTests
{
    private const int CiRuns = 12;

    /// <summary>What the CI seeds must keep reaching: a generator change that stops reaching one fails here.</summary>
    private static readonly string[] CoveredPaths =
    [
        "edit:renameProperty", "edit:addOption", "edit:renameOption", "edit:recolorOption", "edit:removeOption",
        "edit:ignoreCaseWithDuplicates", "edit:tagGroup", "edit:moveNode", "edit:changeType", "step:Delete",
        "step:Use", "step:EntityState", "step:RestoreDatabase", "step:ResetLink", "step:Partition", "chooser:headless",
        "item:LinkSuggestion", "item:FieldConflict", "item:DeletedThere", "item:TypeChange", "undo",
    ];

    [TestMethod]
    [Timeout(900_000)]
    public async Task Seeded_scenarios_converge_on_the_real_stack()
    {
        var first = int.TryParse(Environment.GetEnvironmentVariable("DATASYNC_CONVERGENCE_SEED"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var seed) ? seed : 1;
        var runs = int.TryParse(Environment.GetEnvironmentVariable("DATASYNC_CONVERGENCE_RUNS"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var count) && count > 0 ? count : CiRuns;
        var coverage = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var failures = new List<string>();
        for (var s = first; s < first + runs && failures.Count == 0; s++)
        {
            var scenario = new Scenario(s);
            await scenario.RunAsync();
            foreach (var (what, n) in scenario.Coverage) coverage[what] = coverage.GetValueOrDefault(what) + n;
            if (scenario.Failures.Count > 0) failures.Add(scenario.Report());
        }

        Console.WriteLine(string.Join(", ", coverage.Select(c => $"{c.Key} {c.Value}")));
        Assert.AreEqual(0, failures.Count, "\n" + string.Join("\n\n", failures));
        if (first != 1 || runs < CiRuns) return;
        foreach (var path in CoveredPaths) Assert.IsTrue(coverage.GetValueOrDefault(path) > 0, $"no scenario reached {path}");
    }

    private enum Step
    {
        Create, Edit, Delete, Sync, SyncAll, Resolve, Use, EntityState, Undo, RestoreDatabase, StopLink, StartLink,
        ResetLink, Partition, Heal, Advance,
    }

    private static readonly (Step Step, int Weight)[] Weights =
    [
        (Step.Create, 9), (Step.Edit, 20), (Step.Delete, 3), (Step.Sync, 18), (Step.SyncAll, 5), (Step.Resolve, 8),
        (Step.Use, 6), (Step.EntityState, 2), (Step.Undo, 3), (Step.RestoreDatabase, 1),
        (Step.StopLink, 1), (Step.StartLink, 1), (Step.ResetLink, 2), (Step.Partition, 2), (Step.Heal, 2),
        (Step.Advance, 2),
    ];

    private static readonly string[] Names = ["Genre", "Mood", "Studio", "Series", "Author", "Tags"];
    private static readonly string[] Labels = ["Action", "Drama", "Comedy", "Horror", "Isekai", "Sci-Fi", "Romance"];
    private static readonly string[] Groups = ["Books", "Videos", "Audio", "Images"];
    private static readonly string[] Extensions = [".epub", ".pdf", ".mp4", ".mkv", ".mp3", ".flac", ".jpg", ".zip"];
    private static readonly string[] Colors = ["#ff0000", "#00a000", "#0090ff"];

    private static readonly PropertyType[] Types =
    [
        PropertyType.SingleLineText, PropertyType.SingleChoice, PropertyType.MultipleChoice, PropertyType.Tags,
        PropertyType.Multilevel,
    ];

    /// <summary>One seed: its hosts and links, its steps, and what the invariants found.</summary>
    private sealed class Scenario(int seed)
    {
        private const int MaxRounds = 10;
        private const int MaxDecisions = 300;

        private readonly Random _random = new(seed);
        private readonly SyncWorld _world = new();
        private readonly List<string> _trace = [];
        private readonly Dictionary<SyncHost, List<string>> _backups = [];
        private readonly Dictionary<SyncHost, Snapshot> _snapshots = [];
        private readonly HashSet<(SyncHost Host, int? PropertyId)> _mayRemove = [];
        private readonly List<(SyncHost Reader, SyncHost Source, DataSyncLinkMode Mode)> _links = [];

        public List<string> Failures { get; } = [];
        public Dictionary<string, int> Coverage { get; } = new(StringComparer.Ordinal);
        private IReadOnlyList<SyncHost> Hosts => _world.Hosts;

        private void Count(string what) => Coverage[what] = Coverage.GetValueOrDefault(what) + 1;
        private T Pick<T>(IReadOnlyList<T> from) => from[_random.Next(from.Count)];

        public async Task RunAsync()
        {
            try
            {
                await BuildAsync();
                for (var i = _random.Next(30, 50); i > 0 && Failures.Count == 0; i--)
                {
                    if (i % 8 == 0)
                    {
                        foreach (var host in Hosts)
                        {
                            if (!_backups.TryGetValue(host, out var saved)) _backups[host] = saved = [];
                            saved.Add(await host.BackupDatabaseAsync());
                        }
                    }

                    var step = Draw();
                    _mayRemove.Clear();
                    var what = await ExecuteAsync(step);
                    _trace.Add($"{step}: {what}");
                    Count("step:" + step + (what.StartsWith("no ", StringComparison.Ordinal) ? ":noop" : ""));
                    _world.Clock.Advance(TimeSpan.FromMinutes(1 + _random.Next(10)));
                    await CheckAfterStepAsync(step.ToString());
                }

                if (Failures.Count == 0) await QuiesceAsync();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Failures.Add($"threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
        }

        private Step Draw()
        {
            var pick = _random.Next(Weights.Sum(w => w.Weight));
            foreach (var (step, weight) in Weights)
            {
                if ((pick -= weight) < 0) return step;
            }

            return Step.Sync;
        }

        // ---- shape ---------------------------------------------------------------------------------------------

        private async Task BuildAsync()
        {
            var count = _random.Next(2, 5);
            var shape = count == 2 ? "pair" : Pick(["chain", "star", "mesh"]);
            for (var i = 1; i <= count; i++)
                await _world.AddHostAsync(shape == "star" && i == 1 ? "NAS" : $"PC-{i}", shape == "star" && i == 1);

            var edges = shape switch
            {
                "star" => Enumerable.Range(1, count - 1).Select(i => (Hosts[i], Hosts[0])).ToList(),
                "mesh" => (from i in Enumerable.Range(0, count) from j in Enumerable.Range(i + 1, count - i - 1)
                    select (Hosts[i], Hosts[j])).ToList(),
                _ => Enumerable.Range(1, count - 1).Select(i => (Hosts[i - 1], Hosts[i])).ToList(),
            };
            foreach (var (a, b) in edges)
            {
                // §8.1: a Follow link on a pull cycle of three or more devices can rotate values; a mesh keeps two-way.
                switch (shape == "mesh" ? 0 : _random.Next(4))
                {
                    case 1:
                        await LinkAsync(b, a, DataSyncLinkMode.Follow);
                        break;
                    case 2:
                        await LinkAsync(a, b, DataSyncLinkMode.Follow);
                        await LinkAsync(b, a, DataSyncLinkMode.Follow);
                        break;
                    default:
                        await LinkAsync(a, b, DataSyncLinkMode.TwoWay);
                        break;
                }
            }

            _trace.Add($"shape: {shape}, links: {string.Join("; ", _links.Select(l => $"{l.Reader}←{l.Source}:{l.Mode}"))}");
            foreach (var host in Hosts.Where(h => !h.Headless))
            {
                for (var i = _random.Next(1, 4); i > 0; i--) _trace.Add(await CreateAsync(host));
            }

            await _world.SyncAllAsync();
            await _world.SyncAllAsync();
            foreach (var host in Hosts) _snapshots[host] = await SnapshotAsync(host);
        }

        private async Task LinkAsync(SyncHost reader, SyncHost source, DataSyncLinkMode mode)
        {
            await _world.LinkAsync(reader, source, mode);
            _links.Add((reader, source, mode));
        }

        // ---- steps ---------------------------------------------------------------------------------------------

        private async Task<string> ExecuteAsync(Step step)
        {
            var desktops = Hosts.Where(h => !h.Headless).ToList();
            var host = Pick(desktops);
            switch (step)
            {
                case Step.Create:
                    return await CreateAsync(host);
                case Step.Edit:
                    return await EditAsync(host);
                case Step.Delete:
                {
                    var properties = await host.PropertiesAsync();
                    var groups = await host.GroupsAsync();
                    if (properties.Count + groups.Length == 0) return "no definition to delete";
                    var index = _random.Next(properties.Count + groups.Length);
                    if (index >= properties.Count)
                    {
                        await host.DeleteGroupAsync(groups[index - properties.Count]);
                        return $"{host} deletes group {groups[index - properties.Count].Name}";
                    }

                    _mayRemove.Add((host, properties[index].Id));
                    await host.DeletePropertyAsync(properties[index]);
                    return $"{host} deletes {properties[index].Name}";
                }
                case Step.Sync:
                    host = Pick(Hosts);
                    await _world.SyncAsync(host);
                    return $"{host} pulls";
                case Step.SyncAll:
                    await _world.SyncAllAsync();
                    return "every host pulls";
                case Step.Resolve:
                {
                    var items = new List<DataSyncInboxItemView>();
                    foreach (var desktop in desktops.OrderBy(_ => _random.Next()))
                    {
                        items = [.. await desktop.OpenItemsAsync()];
                        host = desktop;
                        if (items.Count > 0) break;
                    }

                    if (items.Count == 0) return "no item on any desktop";
                    var item = Pick(items);
                    var action = Pick(item.AllowedActions);
                    var (target, custom) = Inputs(item, action);
                    if (action is DataSyncInboxAction.DeleteHere or DataSyncInboxAction.Convert)
                        _mayRemove.Add((host, PropertyIdOf(item)));
                    var problem = await host.ResolveAsync(item, action, target, custom);
                    Count($"item:{item.Type}");
                    return $"{host} resolves {item.Type} '{item.SubjectPath}' of {item.Payload.EntityName} with {action}" +
                           (problem is null ? "" : $" ({problem.Code} {problem.Detail})");
                }
                case Step.Use:
                {
                    var properties = (await host.PropertiesAsync())
                        .Where(p => SyncOptions.WithOptions.Contains(p.Type) && SyncOptions.Read(p).Count > 0).ToList();
                    if (properties.Count == 0) return "no option to use";
                    var property = Pick(properties);
                    var option = Pick(SyncOptions.Read(property));
                    await host.UseAsync(property, option.Id, 1 + _random.Next(3));
                    return $"{host}'s resources use {property.Name}/{option.Label}";
                }
                case Step.EntityState:
                {
                    var entities = (await host.Node.EntitiesAsync()).ToList();
                    if (entities.Count == 0) return "no entity";
                    var entity = Pick(entities);
                    var state = entity.State == DataSyncEntitySyncState.Synced
                        ? Pick([DataSyncEntitySyncState.LocalOnly, DataSyncEntitySyncState.Detached])
                        : DataSyncEntitySyncState.Synced;
                    var start = await host.CallAsync(s => s.SetEntitySyncAsync(entity.Kind, entity.LocalKey,
                        new DataSyncEntitySyncInput(state, null, null, null), default));
                    return $"{host} sets {entity.Kind}/{entity.LocalKey} {state} {start.Problem?.Code}";
                }
                case Step.Undo:
                {
                    var entries = (await host.CallAsync(s => s.GetHistoryAsync(default)))
                        .Where(e => e.UndoState == DataSyncUndoState.Available).ToList();
                    if (entries.Count == 0) return "no entry to undo";
                    var entry = entries[_random.Next(Math.Min(entries.Count, 4))];
                    var start = await host.CallAsync(s => s.StartUndoAsync(entry.Id, default));
                    // The preview reads what the last Refresh recorded, and the task refreshes first: it refuses every
                    // step of an entry whose entity went since then, and says so (§8.11, UndoNotAvailable).
                    await host.Node.RunWriteTasksAsync(id => id == start.TaskId);
                    var undone = (await host.CallAsync(s => s.GetHistoryAsync(default)))
                        .Any(e => e.Id == entry.Id && e.UndoState == DataSyncUndoState.Undone);
                    if (start.Problem is null) Count(undone ? "undo" : "undo:refused");
                    return $"{host} undoes #{entry.Id} {entry.Kind} {start.Problem?.Code}{(undone ? "" : " (not undone)")}";
                }
                case Step.RestoreDatabase:
                {
                    if (!_backups.TryGetValue(host, out var saved) || saved.Count == 0) return "no backup";
                    await host.RestoreDatabaseAsync(Pick(saved));
                    _mayRemove.Add((host, null));
                    return $"{host}'s database alone goes back to a backup";
                }
                case Step.StopLink or Step.StartLink:
                {
                    var link = (await host.CallAsync(s => s.GetLinksAsync(default)))
                        .Where(l => step == Step.StopLink ? l.Mode != DataSyncLinkMode.Off : l.Mode == DataSyncLinkMode.Off)
                        .OrderBy(l => l.Id).FirstOrDefault();
                    if (link is null) return $"no link to {(step == Step.StopLink ? "stop" : "start")}";
                    var mode = step == Step.StopLink ? DataSyncLinkMode.Off : link.LastMode;
                    var result = await host.CallAsync(s => s.UpdateLinkAsync(link.Id,
                        new DataSyncLinkUpdateInput(mode, null), true, default));
                    return $"{host} turns its link to {link.PeerName} {mode} {result.Problem?.Code}";
                }
                case Step.ResetLink:
                {
                    var own = _links.Where(l => l.Reader == host).ToList();
                    if (own.Count == 0) return "no link to reset";
                    var (reader, source, mode) = Pick(own);
                    var link = await reader.Node.LinkToAsync(source.Node);
                    if (link is null) return "no link to reset";
                    var held = (await reader.Node.EntitiesAsync()).Any(e => e.OverlayJson?.Contains("held") == true);
                    if (held) Count("reset:withHolds");
                    Assert.IsNull(await reader.CallAsync(s => s.ResetLinkAsync(link.Id, default)));
                    await _world.LinkAsync(reader, source, mode);
                    return $"{reader} resets its link to {source}{(held ? " while holds exist" : "")} and links again";
                }
                case Step.Partition:
                {
                    var (reader, source, _) = Pick(_links);
                    reader.Node.Client.SetReachable(source.NodeId, false);
                    return $"{reader} cannot reach {source}";
                }
                case Step.Heal:
                    Heal();
                    return "every host reaches every other";
                case Step.Advance:
                {
                    var minutes = _random.Next(3 * 24 * 60);
                    _world.Clock.Advance(TimeSpan.FromMinutes(minutes));
                    return $"{minutes} minutes pass";
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(step));
            }
        }

        private void Heal()
        {
            foreach (var (reader, source, _) in _links) reader.Node.Client.SetReachable(source.NodeId, true);
        }

        private async Task<string> CreateAsync(SyncHost host)
        {
            if (_random.Next(5) == 0)
            {
                var name = Pick(Groups);
                if ((await host.GroupsAsync()).Any(g => g.Name == name)) return "no new group name";
                await host.AddGroupAsync(name, Extensions.OrderBy(_ => _random.Next()).Take(1 + _random.Next(2)).ToArray());
                return $"{host} creates group {name}";
            }

            var free = Names.Except((await host.PropertiesAsync()).Select(p => p.Name)).ToList();
            if (free.Count == 0) return "no new property name";
            var type = Pick(Types);
            var property = await host.AddPropertyAsync(Pick(free), type,
                type == PropertyType.SingleLineText ? [] : Labels.OrderBy(_ => _random.Next()).Take(2 + _random.Next(2)).ToArray());
            if (type == PropertyType.Multilevel && SyncOptions.Read(property) is { Count: > 1 } nodes)
            {
                await host.EditAsync(property, o => o.Select((n, i) => i == 0 ? n : n with { Parent = nodes[0].Id }));
            }

            return $"{host} creates {type} {property.Name}";
        }

        private async Task<string> EditAsync(SyncHost host)
        {
            var groups = await host.GroupsAsync();
            var properties = await host.PropertiesAsync();
            if (groups.Length > 0 && (properties.Count == 0 || _random.Next(4) == 0))
            {
                var group = Pick(groups);
                var extensions = _random.Next(2) == 0
                    ? group.Extensions!.Append(Pick(Extensions)).Distinct().ToList()
                    : group.Extensions!.Skip(1).DefaultIfEmpty(Pick(Extensions)).ToList();
                await host.PutGroupAsync(group, _random.Next(3) == 0 ? group.Name + "+" : group.Name, extensions);
                Count("edit:group");
                return $"{host} edits group {group.Name}";
            }

            if (properties.Count == 0) return "no definition to edit";
            var p = Pick(properties);
            var options = SyncOptions.Read(p);
            var edits = new List<string> { "renameProperty" };
            if (SyncOptions.WithOptions.Contains(p.Type))
                edits.AddRange(["addOption", "ignoreCaseWithDuplicates"]);
            if (options.Count > 0)
                edits.AddRange(["renameOption", "recolorOption", "clearOptionColor", "removeOption"]);
            if (p.Type == PropertyType.Tags && options.Count > 0) edits.AddRange(["tagGroup", "tagGroup"]);
            if (p.Type == PropertyType.Multilevel && options.Count > 1) edits.AddRange(["moveNode", "moveNode"]);
            if (p.Type is PropertyType.SingleChoice or PropertyType.MultipleChoice) edits.Add("changeType");
            var edit = Pick(edits);
            Count("edit:" + edit);
            SyncOption? one = options.Count > 0 ? Pick(options) : null;
            switch (edit)
            {
                case "renameProperty":
                    await host.EditAsync(p, o => o, Pick(Names.Except([p.Name]).ToList()) + " " + _random.Next(9));
                    break;
                case "addOption":
                    await host.EditAsync(p, o => o.Append(new SyncOption(SyncHost.NewId(), Pick(Labels))));
                    break;
                case "renameOption":
                    await host.EditAsync(p, o => o.Select(x => x.Id == one!.Id ? x with { Label = Pick(Labels) } : x));
                    break;
                case "recolorOption" or "clearOptionColor":
                    await host.EditAsync(p, o => o.Select(x => x.Id == one!.Id
                        ? x with { Color = edit == "recolorOption" ? Pick(Colors) : null }
                        : x));
                    break;
                case "removeOption":
                    _mayRemove.Add((host, p.Id));
                    await host.EditAsync(p, o => o.Where(x => x.Id != one!.Id && x.Parent != one.Id));
                    break;
                case "ignoreCaseWithDuplicates":
                    // A case duplicate first, then IgnoreCase flips: the service folds what it now takes as one label.
                    _mayRemove.Add((host, p.Id));
                    var twin = options.Count > 0 ? options[0].Label.ToUpperInvariant() : "ACTION";
                    await host.PutAsync(p, p.Name, !SyncOptions.IgnoreCase(p),
                        [.. options, new SyncOption(SyncHost.NewId(), twin)]);
                    break;
                case "tagGroup":
                    await host.EditAsync(p, o => o.Select(x => x.Id == one!.Id
                        ? x with { Group = x.Group switch { null => "", "" => Pick(Groups), _ => null } }
                        : x));
                    break;
                case "moveNode":
                {
                    var descendants = Descendants(options, one!.Id);
                    var parents = options.Where(x => !descendants.Contains(x.Id) && x.Id != one.Id).ToList();
                    var parent = parents.Count == 0 || _random.Next(3) == 0 ? null : Pick(parents).Id;
                    await host.EditAsync(p, o => o.Select(x => x.Id == one.Id ? x with { Parent = parent } : x));
                    break;
                }
                case "changeType":
                    _mayRemove.Add((host, p.Id));
                    await host.ChangeTypeAsync(p, p.Type == PropertyType.SingleChoice
                        ? PropertyType.MultipleChoice
                        : PropertyType.SingleChoice);
                    break;
            }

            return $"{host} {edit} {p.Name}{(one is null ? "" : "/" + one.Label)}";
        }

        private static HashSet<string> Descendants(List<SyncOption> options, string id)
        {
            var found = new HashSet<string>();
            var next = new Queue<string>([id]);
            while (next.TryDequeue(out var at))
            {
                foreach (var child in options.Where(o => o.Parent == at && found.Add(o.Id))) next.Enqueue(child.Id);
            }

            return found;
        }

        private (string? Target, string? Custom) Inputs(DataSyncInboxItemView item, DataSyncInboxAction action) =>
            action switch
            {
                DataSyncInboxAction.UseCustom => (null, item.SubjectPath == "name" ? $"Custom {_random.Next(3)}" : Pick(Labels)),
                DataSyncInboxAction.Link or DataSyncInboxAction.KeepWithEntity =>
                    (item.Payload.Candidates?.Where(c => c.Updatable).Select(c => c.LocalKey).FirstOrDefault(), null),
                DataSyncInboxAction.KeepRecordLinked => (item.Payload.Records?.FirstOrDefault()?.PrimaryKey, null),
                _ => (null, null),
            };

        private static int? PropertyIdOf(DataSyncInboxItemView item) =>
            item.Kind == DataSyncKindIds.CustomProperty && int.TryParse(item.LocalKey, out var id) ? id : null;

        // ---- quiescence ------------------------------------------------------------------------------------------

        private async Task QuiesceAsync()
        {
            _trace.Add("-- quiescence --");
            _mayRemove.Clear();
            Heal();
            foreach (var host in Hosts)
            {
                foreach (var link in (await host.CallAsync(s => s.GetLinksAsync(default)))
                             .Where(l => l.Mode == DataSyncLinkMode.Off))
                {
                    await host.CallAsync(s => s.UpdateLinkAsync(link.Id, new DataSyncLinkUpdateInput(link.LastMode, null),
                        true, default));
                }
            }

            // A day later every link reconciles in full (§8.8), and again after a day in which a link completed its
            // first contact: the review left the peer's tombstones out (a review never removes, §8.3).
            var firstContacts = "";
            for (var pass = 0; pass < 4 && Failures.Count == 0; pass++)
            {
                if (pass > 0)
                {
                    var now = await FirstContactsAsync();
                    if (pass > 1 && now == firstContacts) break;
                    firstContacts = now;
                    _trace.Add("-- a day later: every link's daily full reconciliation (§8.8) --");
                    _world.Clock.Advance(TimeSpan.FromHours(25));
                }

                await StabilizeAsync("before the decisions", failOnChurn: false);
                for (var decisions = 0;; decisions++)
                {
                    if (decisions == MaxDecisions)
                    {
                        Failures.Add("quiescence: the chooser never ran out of decisions (a decisions livelock)");
                        return;
                    }

                    var (host, item) = await NextDecisionAsync();
                    if (item is null) break;
                    var (action, target) = await DecideAsync(host!, item);
                    _mayRemove.Clear();
                    if (action is DataSyncInboxAction.DeleteHere or DataSyncInboxAction.Convert)
                        _mayRemove.Add((host!, PropertyIdOf(item)));
                    var problem = await host!.ResolveAsync(item, action, target);
                    Count(host.Headless ? "chooser:headless" : "chooser:desktop");
                    Count($"item:{item.Type}");
                    _trace.Add($"chooser: {host} resolves {item.Type} '{item.SubjectPath}' of {item.Payload.EntityName} " +
                               $"with {action} {problem?.Code}");
                    await CheckAfterStepAsync("a decision");
                    if (Failures.Count > 0) return;
                    await StabilizeAsync("after a decision", failOnChurn: false);
                }

                if (!await StabilizeAsync("the final rounds", failOnChurn: true)) return;
            }

            // I2: one more round issues no revision and no Seq.
            var before = await CountersAsync();
            await _world.SyncAllAsync();
            var after = await CountersAsync();
            if (before != after) Failures.Add($"I2: one more round changed counters:\n  {before}\n  {after}");

            await CheckEqualityAsync();
            await CheckNothingWaitsAsync();
        }

        /// <summary>Rounds — restore choices, resumes, reviews, a pull by every host — until nothing changes.</summary>
        private async Task<bool> StabilizeAsync(string phase, bool failOnChurn)
        {
            var before = await _world.DigestAsync();
            for (var round = 0; round < MaxRounds; round++)
            {
                foreach (var host in Hosts) await TendAsync(host);
                await _world.SyncAllAsync();
                await CheckAfterStepAsync(phase);
                if (Failures.Count > 0) return false;
                var after = await _world.DigestAsync();
                if (after == before) return true;
                before = after;
            }

            if (failOnChurn) Failures.Add($"quiescence ({phase}): the hosts never stopped changing (a ping-pong):\n{before}");
            return false;
        }

        /// <summary>What a person does about a paused link, a restore and a review waiting on a host.</summary>
        private async Task TendAsync(SyncHost host)
        {
            if ((await host.CallAsync(s => s.GetRestoreAsync(default))).Pending)
            {
                var choice = seed % 2 == 0 ? DataSyncRestoreChoice.ThisDeviceWins : DataSyncRestoreChoice.OthersWin;
                await host.RunAsync(s => s.ChooseRestoreAsync(choice, null, default));
                _mayRemove.Add((host, null));
                Count("restoreChoice:" + choice);
                _trace.Add($"quiescence: {host} chooses {choice}");
            }

            foreach (var link in await host.CallAsync(s => s.GetLinksAsync(default)))
            {
                if (link.State == DataSyncLinkState.AwaitingReview)
                {
                    var source = Hosts.Single(h => h.NodeId == link.PeerNodeId);
                    await host.StartFirstSyncAsync(source);
                    continue;
                }

                if (link.State != DataSyncLinkState.Paused) continue;
                var action = link.PausedReason switch
                {
                    // A restored peer (§9.5) is resumed from the start; only a reset one is asked for access again.
                    DataSyncPauseReason.PeerReset when link.PausedDetail != DataSyncLinkService.RestoredDetail =>
                        DataSyncResumeAction.AskAccessAgain,
                    _ => DataSyncResumeAction.Resume,
                };
                var resumed = await host.CallAsync(s => s.ResumeLinkAsync(link.Id, action, default));
                Count($"pause:{link.PausedReason}");
                _trace.Add($"quiescence: {host} resumes its link to {link.PeerName} ({link.PausedReason}) with {action} " +
                           resumed.Problem?.Code);
            }
        }

        /// <summary>The next open item: on a desktop first; on a headless host only when no desktop has one left.</summary>
        private async Task<(SyncHost? Host, DataSyncInboxItemView? Item)> NextDecisionAsync()
        {
            foreach (var host in Hosts.OrderBy(h => h.Headless))
            {
                if ((await host.OpenItemsAsync()).OrderBy(i => i.Id).FirstOrDefault() is { } item) return (host, item);
            }

            return (null, null);
        }

        /// <summary>
        /// The deterministic chooser: an answer that settles the question for good. A name match links only to a
        /// definition this host created and never linked (two hosts made it apart); any other keeps both, since linking
        /// it would merge lineages another host keeps apart.
        /// </summary>
        private static async Task<(DataSyncInboxAction, string?)> DecideAsync(SyncHost host, DataSyncInboxItemView item)
        {
            var allowed = item.AllowedActions;
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
                    var entities = await host.Node.EntitiesAsync();
                    var aliased = (await host.Node.KeysAsync()).Where(k => k.Key.Key != null)
                        .GroupBy(k => k.Value).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
                    var fresh = item.Payload.Candidates?.Where(c => c.Updatable).FirstOrDefault(c => entities.Any(e =>
                        e.Kind == item.Kind && e.LocalKey == c.LocalKey && e.OriginNodeId == host.NodeId &&
                        !aliased.Contains(e.Id)));
                    return fresh is not null && allowed.Contains(DataSyncInboxAction.Link)
                        ? (DataSyncInboxAction.Link, fresh.LocalKey)
                        : (DataSyncInboxAction.KeepBoth, null);
                }
                case DataSyncInboxItemType.IdentityConflict when allowed.Contains(DataSyncInboxAction.KeepWithEntity):
                    return (DataSyncInboxAction.KeepWithEntity, item.Payload.Candidates!.Where(c => c.Updatable)
                        .Select(c => c.LocalKey).Order(StringComparer.Ordinal).First());
                case DataSyncInboxItemType.IdentityConflict:
                    return allowed.Contains(DataSyncInboxAction.KeepRecordLinked)
                        ? (DataSyncInboxAction.KeepRecordLinked, item.Payload.Records!
                            .Select(r => r.PrimaryKey).Order(StringComparer.Ordinal).First())
                        : (DataSyncInboxAction.Detach, null);
                case DataSyncInboxItemType.MassChildDeletion:
                    return (DataSyncInboxAction.ApplyAll, null);
                case DataSyncInboxItemType.SuspectedLostUpdate:
                    return (DataSyncInboxAction.Publish, null);
                default:
                    return (allowed[0], null);
            }
        }

        // ---- invariants ----------------------------------------------------------------------------------------

        private sealed record Snapshot(Dictionary<int, (string Name, HashSet<string> Options)> Properties,
            Dictionary<int, HashSet<string>> Usage);

        private static async Task<Snapshot> SnapshotAsync(SyncHost host) => new(
            (await host.PropertiesAsync()).ToDictionary(p => p.Id,
                p => (p.Name, SyncOptions.Read(p).Select(o => o.Id).ToHashSet(StringComparer.Ordinal))),
            await host.UsageAsync());

        /// <summary>I8 on every host, and I4: what resources used is still there, unless a person took it away.</summary>
        private async Task CheckAfterStepAsync(string when)
        {
            foreach (var host in Hosts)
            {
                var keys = new List<(string Kind, string Key)>();
                await host.Node.DbAsync(async db =>
                {
                    keys.AddRange(await db.DataSyncEntities.AsNoTracking().Select(e => new { e.Kind, e.SyncKey })
                        .Select(e => ValueTuple.Create(e.Kind, e.SyncKey)).ToListAsync());
                    keys.AddRange(await db.DataSyncKeyAliases.AsNoTracking().Select(a => new { a.Kind, a.AliasKey })
                        .Select(a => ValueTuple.Create(a.Kind, a.AliasKey)).ToListAsync());
                    return 0;
                });
                foreach (var twice in keys.GroupBy(k => k).Where(g => g.Count() > 1))
                    Failures.Add($"I8 after {when}: {host} has key {twice.Key.Key[..8]} on {twice.Count()} rows");

                var now = await SnapshotAsync(host);
                if (_mayRemove.Contains((host, null)) || !_snapshots.TryGetValue(host, out var before))
                {
                    _snapshots[host] = now;
                    continue;
                }

                foreach (var (id, used) in before.Usage)
                {
                    if (!before.Properties.TryGetValue(id, out var property) || _mayRemove.Contains((host, id))) continue;
                    if (!now.Properties.TryGetValue(id, out var still))
                    {
                        Failures.Add($"I4 after {when}: {host} lost {property.Name}, which resources use");
                        continue;
                    }

                    foreach (var option in used.Where(o => property.Options.Contains(o) && !still.Options.Contains(o)))
                        Failures.Add($"I4 after {when}: {host} lost an option of {property.Name} resources use ({option})");
                }

                _snapshots[host] = now;
            }
        }

        private async Task<string> FirstContactsAsync()
        {
            var parts = new List<string>();
            foreach (var host in Hosts)
            {
                foreach (var link in await host.Node.DbAsync(db => db.DataSyncLinks.AsNoTracking().ToListAsync()))
                    parts.Add($"{host}/{link.Id}:{link.FirstContactCompletedAtUtc:O}");
            }

            return string.Join(", ", parts);
        }

        private async Task<string> CountersAsync()
        {
            var parts = new List<string>();
            foreach (var host in Hosts)
            {
                var state = await host.LocalStateAsync();
                parts.Add($"{host} {state.ActorId}:{state.ActorCounter} seq {state.LastSeq}");
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// I1 and I10: within hosts that pull each other both ways, an entity synced everywhere has one comparison
        /// form — its children included — and one live anywhere is live everywhere, unless a host keeps it out of sync.
        /// </summary>
        private async Task CheckEqualityAsync()
        {
            var rows = new Dictionary<SyncHost, List<DataSyncEntityDbModel>>();
            var keys = new Dictionary<SyncHost, Dictionary<(string Kind, string Key), long>>();
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            var twoWay = new HashSet<(SyncHost, SyncHost)>();
            foreach (var host in Hosts)
            {
                rows[host] = await host.Node.EntitiesAsync(includeTombstones: true);
                keys[host] = await host.Node.KeysAsync();
                foreach (var b in await host.Node.DbAsync(db => db.DataSyncPeerBases.AsNoTracking()
                             .Where(b => b.State == DataSyncBaseState.Excluded).ToListAsync()))
                {
                    excluded.Add(b.SyncKey);
                    foreach (var key in System.Text.Json.JsonSerializer.Deserialize<List<string>>(b.ExclusionKeysJson ?? "[]")!)
                        excluded.Add(key);
                }

                foreach (var link in await host.Node.DbAsync(db => db.DataSyncLinks.AsNoTracking().ToListAsync()))
                {
                    if (link.Mode != DataSyncLinkMode.Off && link.GetEffectiveMode() == DataSyncLinkMode.TwoWay)
                        twoWay.Add((host, Hosts.Single(h => h.NodeId == link.PeerNodeId)));
                }
            }

            foreach (var (a, b) in twoWay.Where(p => twoWay.Contains((p.Item2, p.Item1))))
            {
                foreach (var row in rows[a].Where(r => r.DeletedAtUtc is null && r.State == DataSyncEntitySyncState.Synced))
                {
                    var name = $"{row.Kind}/{row.LocalKey}";
                    var aKeys = keys[a].Where(k => k.Value == row.Id).Select(k => k.Key.Key).ToList();
                    if (aKeys.Any(excluded.Contains)) continue;
                    var other = aKeys.Select(k => keys[b].TryGetValue((row.Kind, k), out var id) ? id : (long?) null)
                        .FirstOrDefault(id => id is not null) is { } bId
                        ? rows[b].Single(r => r.Id == bId)
                        : null;
                    // Linked elsewhere with a key a host keeps out of sync: not synced on all of them (§5.2, row E).
                    if (other is { State: not DataSyncEntitySyncState.Synced } ||
                        other?.TombstoneKind == DataSyncTombstoneKind.UndoneCreate ||
                        (other is not null && keys[b].Any(k => k.Value == other.Id && excluded.Contains(k.Key.Key))))
                        continue;
                    if (other is null || other.DeletedAtUtc is not null)
                    {
                        var bases = await b.Node.DbAsync(db => db.DataSyncPeerBases.AsNoTracking()
                            .Where(x => aKeys.Contains(x.SyncKey)).ToListAsync());                        Failures.Add($"I1: {name} is live on {a} but {(other is null ? "unknown" : "deleted")} on {b}: " +
                                     $"keys [{string.Join(",", aKeys.Select(k => k[..8]))}] origin {row.OriginNodeId[..6]} " +
                                     $"vv {row.VvJson} created-by-sync {row.CreatedBySync}; {b}'s row: " +
                                     $"{other?.TombstoneKind} served {other?.TombstoneServed} seq {other?.Seq} vv " +
                                     $"{other?.VvJson}; {b}'s bases: " +
                                     string.Join("; ", bases.Select(x => $"{x.State}/{x.ExclusionReason}/{x.PendingReason}")));
                    }
                    else if (other.SharedHash != row.SharedHash)
                        Failures.Add($"I1: {name} differs between {a} ({row.VvJson}) and {b} ({other.VvJson})");
                }
            }
        }

        /// <summary>I5: after quiescence nothing waits anywhere, and a headless host never notified anyone.</summary>
        private async Task CheckNothingWaitsAsync()
        {
            foreach (var host in Hosts)
            {
                foreach (var item in await host.OpenItemsAsync())
                    Failures.Add($"I5: {host} still has {item.Type} '{item.SubjectPath}' of {item.Payload.EntityName} open");
                foreach (var link in (await host.CallAsync(s => s.GetLinksAsync(default)))
                             .Where(l => l.State != DataSyncLinkState.Active))
                    Failures.Add($"quiescence: {host}'s link to {link.PeerName} is {link.State} {link.PausedReason}");
                if (!host.Headless) continue;
                var notified = await host.Node.InScopeAsync(sp => sp.GetRequiredService<INotificationService>()
                    .SearchAsync(new NotificationSearchInputModel { PageSize = 100 }));
                if (notified.Data?.Count > 0) Failures.Add($"I5: headless {host} created {notified.Data.Count} notifications");
            }
        }

        public string Report() =>
            $"seed {seed}: {Failures.Count} failure(s)\n  " + string.Join("\n  ", Failures.Take(8)) + "\nsteps:\n  " +
            string.Join("\n  ", _trace) + "\nhosts:\n" + _world.DigestAsync(true).GetAwaiter().GetResult();
    }
}
