using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Abstractions;

/// <summary>
/// Typed base every codec derives from (v3.1 §2.2, extended by §2.3). Kinds implement the typed members; the
/// non-generic interface members cast, delegate and check, so a wrong content type fails with a message naming
/// the expected type and a codec's own bugs never pass as results.
/// </summary>
public abstract class DataSyncKindCodec<TContent> : IDataSyncKindCodec where TContent : class
{
    public abstract DataSyncKindDescriptor Descriptor { get; }
    public abstract int ComparisonFormVersion { get; }

    // ---- typed members a kind implements --------------------------------------------------

    /// <summary>Peer validation. The returned Content is a TContent or null (then Held is set).</summary>
    protected abstract CodecReadResult ReadCore(JsonObject content, DataSyncLimits limits);

    public abstract TContent ReadLocal(JsonObject content);
    public abstract JsonObject Write(TContent content);
    public abstract string NameOf(TContent content);
    public virtual string? SubtypeOf(TContent content) => null;
    public abstract int ChildCountOf(TContent content);
    public abstract DataSyncNaturalMatch MatchNatural(TContent incoming, TContent local);
    public abstract EntityDiff Diff(TContent local, TContent incoming);
    public abstract MergeResult Merge(TContent local, TContent incoming, IReadOnlySet<string> acceptedChangeIds);
    public abstract MergeResult PrepareCreate(TContent incoming, string? nameOverride);

    /// <summary>
    /// §3.5. The returned Content must be a TContent, or null with Held set when a reader would hold the entity
    /// (step 4); HeldDetail only with Held.
    /// </summary>
    public abstract DataSyncPublishable Publish(TContent localContent, DataSyncOverlay overlay, bool childrenLocal);

    /// <summary>§3.4, over published (validated) content.</summary>
    public abstract JsonObject ComparisonForm(TContent publishedContent, string? orderKey, bool childrenLocal);

    /// <summary>
    /// §8.5.4. <paramref name="baseContent"/>, <paramref name="local"/> and <paramref name="remote"/> are the
    /// input's contents, already checked and cast; <paramref name="input"/> carries every other argument.
    /// </summary>
    protected abstract IReadOnlyList<string> ChildDeletionCandidates(TContent? baseContent, TContent local,
        TContent remote, DataSyncChildCandidatesInput input);

    /// <summary>
    /// §8.5. <paramref name="baseContent"/>, <paramref name="local"/> and <paramref name="remote"/> are the
    /// input's contents, already checked and cast; <paramref name="input"/> carries every other argument. The
    /// returned Merged must be a TContent.
    /// </summary>
    protected abstract DataSyncMerge3Result Merge3(TContent? baseContent, TContent local, TContent remote,
        DataSyncMerge3Input input);

    public abstract IReadOnlyList<DataSyncChildInfo> ChildrenOf(TContent content);

    /// <summary>
    /// <see cref="IDataSyncKindCodec.MapChildrenByClass"/>: labels compared ordinally unless the kind folds them (a
    /// property that ignores case).
    /// </summary>
    public virtual IReadOnlyDictionary<string, string> MapChildrenByClass(TContent from, TContent to) =>
        DataSyncChildClassMap.Map(ChildrenOf(from), ChildrenOf(to), label => label);

    /// <summary>
    /// v1: identity at the current version. A kind that bumps SchemaVersion overrides this and chains its steps;
    /// forgetting to is caught here (an older version is held, not misread).
    /// </summary>
    public virtual JsonObject Upgrade(JsonObject content, int fromSchemaVersion)
    {
        if (fromSchemaVersion > Descriptor.SchemaVersion)
            throw new DataSyncHeldException(DataSyncHeldReason.NewerSchema);
        if (fromSchemaVersion != Descriptor.SchemaVersion)
            throw new DataSyncHeldException(DataSyncHeldReason.Invalid,
                $"{Descriptor.Kind}: no upgrade from schema {fromSchemaVersion} to {Descriptor.SchemaVersion}.");
        return content;
    }

    // ---- the non-generic contract ----------------------------------------------------------

    public CodecReadResult Read(JsonObject content, DataSyncLimits limits)
    {
        CodecReadResult result;
        try
        {
            result = ReadCore(content, limits);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or FormatException or InvalidOperationException
                                      or ArgumentException or InvalidCastException or OverflowException
                                      or KeyNotFoundException or NullReferenceException
                                      or IndexOutOfRangeException)
        {
            // Last resort: a codec that throws on odd peer input holds that entity, never the pull.
            return new CodecReadResult(null, DataSyncHeldReason.Invalid, [e.Message], []);
        }

        if (result.Content is not null and not TContent)
            throw new InvalidOperationException(
                $"{Descriptor.Kind}: ReadCore returned {result.Content.GetType().Name}, {typeof(TContent).Name} expected.");
        if ((result.Content is null) != (result.Held is not null))
            throw new InvalidOperationException($"{Descriptor.Kind}: Content and Held must be exclusive.");
        return result;
    }

    public IReadOnlyList<string> ChildDeletionCandidates(DataSyncChildCandidatesInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (baseContent, local, remote) = CastMergeContents(input.Base, input.Local, input.Remote, input.Mode3);
        return ChildDeletionCandidates(baseContent, local, remote, input);
    }

    public DataSyncMerge3Result Merge3(DataSyncMerge3Input input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (baseContent, local, remote) = CastMergeContents(input.Base, input.Local, input.Remote, input.Mode3);
        var result = Merge3(baseContent, local, remote, input);
        return result.Merged is TContent
            ? result
            : throw new InvalidOperationException(
                $"{Descriptor.Kind}: Merge3 produced {result.Merged?.GetType().Name ?? "null"}, {typeof(TContent).Name} expected.");
    }

    object IDataSyncKindCodec.ReadLocal(JsonObject content) => ReadLocal(content);
    JsonObject IDataSyncKindCodec.Write(object content) => Write(Cast(content));
    string IDataSyncKindCodec.NameOf(object content) => NameOf(Cast(content));
    string? IDataSyncKindCodec.SubtypeOf(object content) => SubtypeOf(Cast(content));
    int IDataSyncKindCodec.ChildCountOf(object content) => ChildCountOf(Cast(content));

    DataSyncNaturalMatch IDataSyncKindCodec.MatchNatural(object incoming, object local) =>
        MatchNatural(Cast(incoming), Cast(local));

    EntityDiff IDataSyncKindCodec.Diff(object local, object incoming) => Diff(Cast(local), Cast(incoming));

    MergeResult IDataSyncKindCodec.Merge(object local, object incoming, IReadOnlySet<string> acceptedChangeIds) =>
        Checked(Merge(Cast(local), Cast(incoming), acceptedChangeIds));

    MergeResult IDataSyncKindCodec.PrepareCreate(object incoming, string? nameOverride) =>
        Checked(PrepareCreate(Cast(incoming), nameOverride));

    DataSyncPublishable IDataSyncKindCodec.Publish(object localContent, DataSyncOverlay overlay, bool childrenLocal)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var result = Publish(Cast(localContent), overlay, childrenLocal);
        if (result.Content is not null and not TContent)
            throw new InvalidOperationException(
                $"{Descriptor.Kind}: Publish produced {result.Content.GetType().Name}, {typeof(TContent).Name} expected.");
        if ((result.Content is null) != (result.Held is not null))
            throw new InvalidOperationException($"{Descriptor.Kind}: published Content and Held must be exclusive.");
        if (result.HeldDetail is not null && result.Held is null)
            throw new InvalidOperationException($"{Descriptor.Kind}: a published HeldDetail needs Held.");
        return result;
    }

    JsonObject IDataSyncKindCodec.ComparisonForm(object publishedContent, string? orderKey, bool childrenLocal) =>
        ComparisonForm(Cast(publishedContent), orderKey, childrenLocal);

    IReadOnlyList<DataSyncChildInfo> IDataSyncKindCodec.ChildrenOf(object content) => ChildrenOf(Cast(content));

    IReadOnlyDictionary<string, string> IDataSyncKindCodec.MapChildrenByClass(object from, object to) =>
        MapChildrenByClass(Cast(from), Cast(to));

    // ---- unknown top-level members (§3.5 step 5, §3.4, §8.9) --------------------------------

    /// <summary>
    /// The top-level content members this codec reads and writes, present or not in a given content; an unknown
    /// member carried for preservation never takes one of these names. Members <see cref="Write"/> emitted are
    /// protected whatever this returns.
    /// </summary>
    protected virtual IReadOnlyCollection<string> KnownContentMembers => [];

    public JsonObject WritePublished(object publishedContent, JsonObject? unknown) =>
        Canonical.DataSyncContentForms.WithUnknown(Write(Cast(publishedContent)), unknown, KnownContentMembers);

    public JsonObject ComparisonForm(object publishedContent, string? orderKey, bool childrenLocal, JsonObject? unknown) =>
        Canonical.DataSyncContentForms.WithUnknown(ComparisonForm(Cast(publishedContent), orderKey, childrenLocal),
            unknown, KnownContentMembers);

    public string SharedHash(object publishedContent, string? orderKey, bool childrenLocal, JsonObject? unknown) =>
        Canonical.ContentHash.Of(ComparisonForm(publishedContent, orderKey, childrenLocal, unknown));

    protected static TContent Cast(object? content) => content as TContent
        ?? throw new ArgumentException(
            $"{typeof(TContent).Name} expected, got {content?.GetType().Name ?? "null"}.", nameof(content));

    private (TContent? Base, TContent Local, TContent Remote) CastMergeContents(object? baseContent, object local,
        object remote, DataSyncMerge3Mode mode3)
    {
        // Convert may come without a base (a key-bound entity with none, §8.5.6): name then merges by the NoBase rule.
        if (baseContent is null && mode3 == DataSyncMerge3Mode.ThreeWay)
            throw new ArgumentException($"{Descriptor.Kind}: a {mode3} merge needs a base.", nameof(baseContent));
        return (baseContent is null ? null : Cast(baseContent), Cast(local), Cast(remote));
    }

    private MergeResult Checked(MergeResult result) => result.Content is TContent
        ? result
        : throw new InvalidOperationException(
            $"{Descriptor.Kind}: merge produced {result.Content?.GetType().Name ?? "null"}, {typeof(TContent).Name} expected.");
}
