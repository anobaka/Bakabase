using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Components.Properties.Choice;

namespace Bakabase.Modules.Property.Components.Accessors;

/// <summary>
/// MultipleChoice property accessor.
/// For external access, use PropertySystem.Builtin and PropertyValueFactory.
/// </summary>
internal class MultipleChoicePropertyAccessor : BuiltinPropertyAccessor<List<string>>
{
    public MultipleChoicePropertyAccessor(ResourceProperty prop) : base(prop)
    {
    }

    // === DbValue - Build ===

    /// <summary>
    /// Build DbValue directly from raw values (IDs/UUIDs).
    /// Use when you already have the IDs.
    /// </summary>
    public List<string>? BuildDbValue(IEnumerable<string>? values) =>
        PropertyValueFactory.MultipleChoice.BuildDbValue(values);

    /// <summary>
    /// Build DbValue directly from raw values.
    /// </summary>
    public List<string>? BuildDbValue(params string[] values) =>
        PropertyValueFactory.MultipleChoice.BuildDbValue(values);

    /// <summary>
    /// Build serialized DbValue directly from raw values.
    /// </summary>
    public string? BuildDbValueSerialized(IEnumerable<string>? values) =>
        PropertyValueFactory.MultipleChoice.BuildDbValueSerialized(values);

    /// <summary>
    /// Build serialized DbValue directly from raw values.
    /// </summary>
    public string? BuildDbValueSerialized(params string[] values) =>
        PropertyValueFactory.MultipleChoice.BuildDbValueSerialized(values);

    // === DbValue - Match ===

    /// <summary>
    /// Match labels to options and return DbValue (list of Choice.Value/IDs).
    /// </summary>
    /// <param name="options">Choice options containing label-to-value mapping</param>
    /// <param name="labels">Labels to match</param>
    /// <param name="addOnMiss">If true, adds new choices to options when labels not found</param>
    /// <returns>List of matched Choice.Value (IDs), or null if none matched</returns>
    public List<string>? MatchDbValue(MultipleChoicePropertyOptions? options, IEnumerable<string>? labels, bool addOnMiss = false) =>
        PropertyValueFactory.MultipleChoice.MatchDbValue(options, labels, addOnMiss);

    /// <summary>
    /// Match labels to options and return serialized DbValue.
    /// </summary>
    public string? MatchDbValueSerialized(MultipleChoicePropertyOptions? options, IEnumerable<string>? labels, bool addOnMiss = false) =>
        PropertyValueFactory.MultipleChoice.MatchDbValueSerialized(options, labels, addOnMiss);

    // === BizValue - Build ===

    /// <summary>
    /// Build BizValue directly from raw values (labels).
    /// Use when you already have the labels.
    /// </summary>
    public List<string>? BuildBizValue(IEnumerable<string>? values) =>
        PropertyValueFactory.MultipleChoice.BuildBizValue(values);

    /// <summary>
    /// Build BizValue directly from raw values.
    /// </summary>
    public List<string>? BuildBizValue(params string[] values) =>
        PropertyValueFactory.MultipleChoice.BuildBizValue(values);

    /// <summary>
    /// Build serialized BizValue directly from raw values.
    /// </summary>
    public string? BuildBizValueSerialized(IEnumerable<string>? values) =>
        PropertyValueFactory.MultipleChoice.BuildBizValueSerialized(values);

    /// <summary>
    /// Build serialized BizValue directly from raw values.
    /// </summary>
    public string? BuildBizValueSerialized(params string[] values) =>
        PropertyValueFactory.MultipleChoice.BuildBizValueSerialized(values);

    // === BizValue - Match ===

    /// <summary>
    /// Match DbValues (IDs) to options and return BizValue (list of Choice.Labels).
    /// </summary>
    /// <param name="options">Choice options containing value-to-label mapping</param>
    /// <param name="dbValues">DbValues (IDs) to match</param>
    /// <returns>List of matched Choice.Labels, or null if none matched</returns>
    public List<string>? MatchBizValue(MultipleChoicePropertyOptions? options, List<string>? dbValues) =>
        PropertyValueFactory.MultipleChoice.MatchBizValue(options, dbValues);

    /// <summary>
    /// Match DbValues to options and return serialized BizValue.
    /// </summary>
    public string? MatchBizValueSerialized(MultipleChoicePropertyOptions? options, List<string>? dbValues) =>
        PropertyValueFactory.MultipleChoice.MatchBizValueSerialized(options, dbValues);
}
