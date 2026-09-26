using System.Text;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Merging;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

/// <summary>
/// A node's whole state as one canonical text: equal texts mean equal states (invariants I2, I3 and I6, and the
/// quiescence check). <see cref="Describe"/> is the short form a failure message prints.
/// </summary>
internal static class SimDigest
{
    public static string Of(SimNode node)
    {
        var db = node.Db;
        var text = new StringBuilder();
        var local = db.Local;
        text.Append($"local {local.ActorId} {local.ActorCounter} {local.LastSeq} {local.Generation} {local.RestoreReason} " +
                    $"{string.Join(",", local.RetiredActors.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => r.Key + ":" + r.Value))}\n");
        foreach (var row in db.Rows.OrderBy(r => r.Kind, StringComparer.Ordinal)
                     .ThenBy(r => r.HasSideRow ? r.Primary.Value : "~" + r.LocalKey, StringComparer.Ordinal))
        {
            var content = row.Content is null ? "-" : CanonicalJson.Serialize(SimKinds.Of(row.Kind).Codec.Write(row.Content));
            text.Append($"row {row.Kind} {row.LocalKey} [{string.Join(",", row.Keys.Select(k => k.Value[..8]))}] {content} " +
                        $"{row.Vv} {row.Seq} {row.State} {row.Deleted}/{row.Served}/{row.TombstoneKind} {row.OrderKey} " +
                        $"{row.PublishHeld} {row.CreatedBySync} cl:{row.ChildrenLocal} {row.LastEditor?.ActorId} " +
                        $"lo[{string.Join(",", row.Overlay.LocalOnlyChildren)}] held[{string.Join(",", row.Overlay.HeldChildren.Select(h => h.ChildId + "@" + h.LinkId))}]\n");
        }

        foreach (var (kind, order) in db.Order.OrderBy(o => o.Key, StringComparer.Ordinal))
            text.Append($"order {kind} {string.Join(",", order)}\n");
        foreach (var link in db.Links.Values.OrderBy(l => l.Id))
        {
            text.Append($"link {link.Id} {link.Peer.NodeId} {link.Mode} {link.Paused} {link.OnceFlags} " +
                        $"{string.Join(",", link.Cursors.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + ":" + c.Value))} " +
                        $"att {link.PeerAttention}\n");
            foreach (var b in link.Bases.Values.OrderBy(b => b.Kind, StringComparer.Ordinal).ThenBy(b => b.Key.Value, StringComparer.Ordinal))
            {
                text.Append($"  base {b.Kind} {b.Key.Value[..8]} {b.State} {b.Exclusion} {b.Vv} " +
                            $"{b.Pending?.Reason} {b.Pending?.RecordHash} {b.Pending?.EvaluatedAtLocalSeq} {b.Pending?.Flags} " +
                            $"{string.Join(",", b.ChildMap.OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => m.Key + ">" + m.Value))}\n");
            }
        }

        foreach (var item in db.Items.OrderBy(i => i.Id))
            text.Append($"item {item.Id} {item.Type} {item.Kind} {item.Key.Value[..8]} {item.Subject} {item.LinkId} {item.Closure} {item.Token} {item.RecordVv}\n");
        foreach (var ((kind, key), usage) in db.Usage.OrderBy(u => u.Key.Kind, StringComparer.Ordinal).ThenBy(u => u.Key.LocalKey, StringComparer.Ordinal))
            text.Append($"usage {kind} {key} {string.Join(",", usage.OrderBy(u => u.Key, StringComparer.Ordinal).Select(u => u.Key + ":" + u.Value))}\n");
        foreach (var ((kind, key), values) in db.Values.OrderBy(u => u.Key.Kind, StringComparer.Ordinal).ThenBy(u => u.Key.LocalKey, StringComparer.Ordinal))
            text.Append($"values {kind} {key} {values}\n");
        return text.ToString();
    }

    public static string Of(SimWorld world) => string.Concat(world.Nodes.Select(n =>
        string.Concat(Of(n).Split('\n').Where(l => l.Length > 0).Select(l => $"{n.Name}| {l}\n"))));

    /// <summary>A node's definitions, links and open items in a few lines (failure messages).</summary>
    public static string Describe(SimNode node)
    {
        var text = new StringBuilder();
        text.Append($"  {node.Name}{(node.Headless ? " (headless)" : "")}: actor {node.Actor.Value[..6]}#{node.ActorCounter} seq {node.LastSeq}" +
                    $"{(node.RestorePending ? " RESTORE PENDING" : "")}{(node.Verified ? "" : " unverified")}\n");
        foreach (var row in node.Rows.OrderBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.LocalKey, StringComparer.Ordinal))
        {
            var what = row.Deleted ? $"† {row.TombstoneKind}{(row.Served ? "" : " unserved")}" : row.Shown;
            var shared = row.Content is null
                ? ""
                : " form " + DataSyncPublication.Of(SimKinds.Of(row.Kind).Codec, row.Content, row.Overlay, row.ChildrenLocal,
                    row.OrderKey, row.Unknown).SharedHash?[7..15];
            text.Append($"    {row.Kind}:{row.LocalKey} [{string.Join(",", row.Keys.Select(k => k.Value[..6]))}] {what} " +
                        $"{row.State}{(row.ChildrenLocal ? " children-local" : "")} vv {row.Vv} ok {row.OrderKey}{shared}\n");
        }

        foreach (var link in node.Links.Values)
        {
            text.Append($"    {link} cursors {string.Join(",", link.Cursors.Select(c => c.Key + ":" + c.Value))}\n");
            foreach (var b in link.Bases.Values.Where(b => b.State != DataSyncBaseState.Normal || b.Pending is not null))
                text.Append($"      base {b.Kind}/{b.Key.Value[..6]} {b.State} {b.Exclusion} pending {b.Pending?.Reason}\n");
        }

        foreach (var item in node.OpenItems) text.Append($"    open {item}\n");
        return text.ToString();
    }
}
