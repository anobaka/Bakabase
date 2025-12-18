using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Components.Properties.Choice;

namespace Bakabase.Modules.Property.Components.Accessors;

/// <summary>
/// SingleChoice property accessor.
/// For external access, use PropertySystem.Builtin and PropertyValueFactory.
/// </summary>
internal class SingleChoicePropertyAccessor : BuiltinPropertyAccessor<string>
{
    public SingleChoicePropertyAccessor(ResourceProperty prop) : base(prop)
    {
    }

    // === DbValue - Build ===

    /// <summary>
    /// Build DbValue directly from raw value (ID/UUID).
    /// Use when you already have the ID.
    /// </summary>
    public string? BuildDbValue(string? value) => PropertyValueFactory.SingleChoice.BuildDbValue(value);

    /// <summary>
    /// Build serialized DbValue directly from raw value.
    /// </summary>
    public string? BuildDbValueSerialized(string? value) => PropertyValueFactory.SingleChoice.BuildDbValueSerialized(value);

    // === DbValue - Match ===

    /// <summary>
    /// Match label to options and return DbValue (Choice.Value/ID).
    /// </summary>
    /// <param name="options">Choice options containing label-to-value mapping</param>
    /// <param name="label">Label to match</param>
    /// <param name="addOnMiss">If true, adds new choice to options when label not found</param>
    /// <returns>The matched Choice.Value (ID), or null if not found</returns>
    public string? MatchDbValue(SingleChoicePropertyOptions? options, string? label, bool addOnMiss = false) =>
        PropertyValueFactory.SingleChoice.MatchDbValue(options, label, addOnMiss);

    /// <summary>
    /// Match label to options and return serialized DbValue.
    /// </summary>
    public string? MatchDbValueSerialized(SingleChoicePropertyOptions? options, string? label, bool addOnMiss = false) =>
        PropertyValueFactory.SingleChoice.MatchDbValueSerialized(options, label, addOnMiss);

    // === BizValue - Build ===

    /// <summary>
    /// Build BizValue directly from raw value (label).
    /// Use when you already have the label.
    /// </summary>
    public string? BuildBizValue(string? value) => PropertyValueFactory.SingleChoice.BuildBizValue(value);

    /// <summary>
    /// Build serialized BizValue directly from raw value.
    /// </summary>
    public string? BuildBizValueSerialized(string? value) => PropertyValueFactory.SingleChoice.BuildBizValueSerialized(value);

    // === BizValue - Match ===

    /// <summary>
    /// Match DbValue (ID) to options and return BizValue (Choice.Label).
    /// </summary>
    /// <param name="options">Choice options containing value-to-label mapping</param>
    /// <param name="dbValue">DbValue (ID) to match</param>
    /// <returns>The matched Choice.Label, or null if not found</returns>
    public string? MatchBizValue(SingleChoicePropertyOptions? options, string? dbValue) =>
        PropertyValueFactory.SingleChoice.MatchBizValue(options, dbValue);

    /// <summary>
    /// Match DbValue to options and return serialized BizValue.
    /// </summary>
    public string? MatchBizValueSerialized(SingleChoicePropertyOptions? options, string? dbValue) =>
        PropertyValueFactory.SingleChoice.MatchBizValueSerialized(options, dbValue);
}
