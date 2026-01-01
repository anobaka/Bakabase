using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Components.Properties.Multilevel;

namespace Bakabase.Modules.Property.Components.Accessors;

/// <summary>
/// Multilevel property accessor.
/// For external access, use PropertySystem.Builtin and PropertyValueFactory.
/// </summary>
internal class MultilevelPropertyAccessor : BuiltinPropertyAccessor<List<List<string>>>
{
    public MultilevelPropertyAccessor(ResourceProperty prop) : base(prop)
    {
    }

    // === DbValue - Build ===

    /// <summary>
    /// Build DbValue directly from raw values (IDs/UUIDs).
    /// Use when you already have the IDs.
    /// </summary>
    public List<string>? BuildDbValue(IEnumerable<string>? values) =>
        PropertyValueFactory.Multilevel.BuildDbValue(values);

    /// <summary>
    /// Build DbValue directly from raw values.
    /// </summary>
    public List<string>? BuildDbValue(params string[] values) =>
        PropertyValueFactory.Multilevel.BuildDbValue(values);

    /// <summary>
    /// Build serialized DbValue directly from raw values.
    /// </summary>
    public string? BuildDbValueSerialized(IEnumerable<string>? values) =>
        PropertyValueFactory.Multilevel.BuildDbValueSerialized(values);

    /// <summary>
    /// Build serialized DbValue directly from raw values.
    /// </summary>
    public string? BuildDbValueSerialized(params string[] values) =>
        PropertyValueFactory.Multilevel.BuildDbValueSerialized(values);

    // === DbValue - Match ===

    /// <summary>
    /// Match label chains to options and return DbValue (list of node IDs).
    /// </summary>
    /// <param name="options">Multilevel options containing label-chain-to-value mapping</param>
    /// <param name="paths">Label chains to match (each path is a list of labels from root to leaf)</param>
    /// <param name="addOnMiss">If true, adds new branches to options when paths not found</param>
    /// <returns>List of matched node IDs, or null if none matched</returns>
    public List<string>? MatchDbValue(MultilevelPropertyOptions? options, IEnumerable<List<string>>? paths, bool addOnMiss = false) =>
        PropertyValueFactory.Multilevel.MatchDbValue(options, paths, addOnMiss);

    /// <summary>
    /// Match label chains to options and return serialized DbValue.
    /// </summary>
    public string? MatchDbValueSerialized(MultilevelPropertyOptions? options, IEnumerable<List<string>>? paths, bool addOnMiss = false) =>
        PropertyValueFactory.Multilevel.MatchDbValueSerialized(options, paths, addOnMiss);

    // === BizValue - Build ===

    /// <summary>
    /// Build BizValue directly from raw values (label chains).
    /// Use when you already have the label paths.
    /// </summary>
    public List<List<string>>? BuildBizValue(IEnumerable<List<string>>? values) =>
        PropertyValueFactory.Multilevel.BuildBizValue(values);

    /// <summary>
    /// Build BizValue directly from raw values.
    /// </summary>
    public List<List<string>>? BuildBizValue(params List<string>[] values) =>
        PropertyValueFactory.Multilevel.BuildBizValue(values);

    /// <summary>
    /// Build serialized BizValue directly from raw values.
    /// </summary>
    public string? BuildBizValueSerialized(IEnumerable<List<string>>? values) =>
        PropertyValueFactory.Multilevel.BuildBizValueSerialized(values);

    /// <summary>
    /// Build serialized BizValue directly from raw values.
    /// </summary>
    public string? BuildBizValueSerialized(params List<string>[] values) =>
        PropertyValueFactory.Multilevel.BuildBizValueSerialized(values);

    // === BizValue - Match ===

    /// <summary>
    /// Match DbValues (IDs) to options and return BizValue (list of label chains).
    /// </summary>
    /// <param name="options">Multilevel options containing value-to-label-chain mapping</param>
    /// <param name="dbValues">DbValues (IDs) to match</param>
    /// <returns>List of label chains (path from root to matched node), or null if none matched</returns>
    public List<List<string>>? MatchBizValue(MultilevelPropertyOptions? options, List<string>? dbValues) =>
        PropertyValueFactory.Multilevel.MatchBizValue(options, dbValues);

    /// <summary>
    /// Match DbValues to options and return serialized BizValue.
    /// </summary>
    public string? MatchBizValueSerialized(MultilevelPropertyOptions? options, List<string>? dbValues) =>
        PropertyValueFactory.Multilevel.MatchBizValueSerialized(options, dbValues);
}
