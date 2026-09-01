using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.Property.Components;

/// <summary>
/// Type-safe helpers for reference-type property values (Choice/Tags/Multilevel),
/// whose DbValues are option ids and BizValues are labels.
///
/// API Pattern:
/// - BuildDbValue/BuildBizValue: Direct construction (when you already have ids/labels)
/// - MatchDbValue/MatchBizValue: Match through options (label &lt;-&gt; id)
/// - *Serialized: Returns the serialized (wire-format) string
///
/// Non-reference types need no factory: their DbValue equals the raw value —
/// serialize with SerializeAsStandardValue directly.
/// </summary>
public static class PropertyValueFactory
{
    private static Bakabase.Abstractions.Models.Domain.Property VirtualProperty(PropertyType type, object options) =>
        new(PropertyPool.Custom, 0, type, null, options);

    private static PropertyValueMatchPolicy Policy(bool addOnMiss) => addOnMiss
        ? PropertyValueMatchPolicy.AutoCreateOptions
        : PropertyValueMatchPolicy.MatchOnly;

    #region Choice Types (SingleChoice, MultipleChoice)

    /// <summary>
    /// SingleChoice property value factory.
    /// DbValue = UUID/ID, BizValue = Label
    /// </summary>
    public static class SingleChoice
    {
        // ============================================================
        // DbValue - Build & Match
        // ============================================================

        /// <summary>
        /// Build DbValue directly from raw value (ID/UUID).
        /// Use when you already have the ID.
        /// </summary>
        public static string? BuildDbValue(string? value) =>
            string.IsNullOrEmpty(value) ? null : value;

        /// <summary>
        /// Build serialized DbValue directly from raw value.
        /// </summary>
        public static string? BuildDbValueSerialized(string? value) =>
            BuildDbValue(value)?.SerializeAsStandardValue(StandardValueType.String);

        /// <summary>
        /// Match label to options and return DbValue (Choice.Value/ID).
        /// </summary>
        /// <param name="options">Choice options containing label-to-value mapping</param>
        /// <param name="label">Label to match</param>
        /// <param name="addOnMiss">If true, adds new choice to options when label not found</param>
        /// <returns>The matched Choice.Value (ID), or null if not found</returns>
        public static string? MatchDbValue(
            SingleChoicePropertyOptions? options,
            string? label,
            bool addOnMiss = false)
        {
            // Thin wrapper over the descriptor — the single implementation of matching.
            if (options == null) return null;
            var property = VirtualProperty(PropertyType.SingleChoice, options);
            var (dbValue, _) = PropertySystem.Property.ToDbValue(property, label, Policy(addOnMiss));
            return dbValue as string;
        }

        /// <summary>
        /// Match label to options and return serialized DbValue.
        /// </summary>
        public static string? MatchDbValueSerialized(
            SingleChoicePropertyOptions? options,
            string? label,
            bool addOnMiss = false) =>
            MatchDbValue(options, label, addOnMiss)?.SerializeAsStandardValue(StandardValueType.String);

        // ============================================================
        // BizValue - Build & Match
        // ============================================================

        /// <summary>
        /// Build BizValue directly from raw value (label).
        /// Use when you already have the label.
        /// </summary>
        public static string? BuildBizValue(string? value) =>
            string.IsNullOrEmpty(value) ? null : value;

        /// <summary>
        /// Build serialized BizValue directly from raw value.
        /// </summary>
        public static string? BuildBizValueSerialized(string? value) =>
            BuildBizValue(value)?.SerializeAsStandardValue(StandardValueType.String);

        /// <summary>
        /// Match DbValue (ID) to options and return BizValue (Choice.Label).
        /// </summary>
        /// <param name="options">Choice options containing value-to-label mapping</param>
        /// <param name="dbValue">DbValue (ID) to match</param>
        /// <returns>The matched Choice.Label, or null if not found</returns>
        public static string? MatchBizValue(
            SingleChoicePropertyOptions? options,
            string? dbValue)
        {
            if (options == null || string.IsNullOrEmpty(dbValue)) return null;
            return PropertySystem.Property.ToBizValue(
                VirtualProperty(PropertyType.SingleChoice, options), dbValue) as string;
        }

        /// <summary>
        /// Match DbValue to options and return serialized BizValue.
        /// </summary>
        public static string? MatchBizValueSerialized(
            SingleChoicePropertyOptions? options,
            string? dbValue) =>
            MatchBizValue(options, dbValue)?.SerializeAsStandardValue(StandardValueType.String);
    }

    /// <summary>
    /// MultipleChoice property value factory.
    /// DbValue = List of UUID/ID, BizValue = List of Label
    /// </summary>
    public static class MultipleChoice
    {
        // ============================================================
        // DbValue - Build & Match
        // ============================================================

        /// <summary>
        /// Build DbValue directly from raw values (IDs/UUIDs).
        /// Use when you already have the IDs.
        /// </summary>
        public static List<string>? BuildDbValue(IEnumerable<string>? values)
        {
            var result = values?.Where(v => !string.IsNullOrEmpty(v)).ToList();
            return result?.Count > 0 ? result : null;
        }

        /// <summary>
        /// Build DbValue directly from raw values.
        /// </summary>
        public static List<string>? BuildDbValue(params string[] values) =>
            BuildDbValue(values.AsEnumerable());

        /// <summary>
        /// Build serialized DbValue directly from raw values.
        /// </summary>
        public static string? BuildDbValueSerialized(IEnumerable<string>? values) =>
            BuildDbValue(values)?.SerializeAsStandardValue(StandardValueType.ListString);

        /// <summary>
        /// Build serialized DbValue directly from raw values.
        /// </summary>
        public static string? BuildDbValueSerialized(params string[] values) =>
            BuildDbValueSerialized(values.AsEnumerable());

        /// <summary>
        /// Match labels to options and return DbValue (list of Choice.Value/IDs).
        /// </summary>
        /// <param name="options">Choice options containing label-to-value mapping</param>
        /// <param name="labels">Labels to match</param>
        /// <param name="addOnMiss">If true, adds new choices to options when labels not found</param>
        /// <returns>List of matched Choice.Value (IDs), or null if none matched</returns>
        public static List<string>? MatchDbValue(
            MultipleChoicePropertyOptions? options,
            IEnumerable<string>? labels,
            bool addOnMiss = false)
        {
            // Thin wrapper over the descriptor — the single implementation of matching.
            if (options == null || labels == null) return null;
            var property = VirtualProperty(PropertyType.MultipleChoice, options);
            var (dbValue, _) = PropertySystem.Property.ToDbValue(property, labels.ToList(), Policy(addOnMiss));
            return dbValue as List<string>;
        }

        /// <summary>
        /// Match labels to options and return serialized DbValue.
        /// </summary>
        public static string? MatchDbValueSerialized(
            MultipleChoicePropertyOptions? options,
            IEnumerable<string>? labels,
            bool addOnMiss = false) =>
            MatchDbValue(options, labels, addOnMiss)?.SerializeAsStandardValue(StandardValueType.ListString);

        // ============================================================
        // BizValue - Build & Match
        // ============================================================

        /// <summary>
        /// Build BizValue directly from raw values (labels).
        /// Use when you already have the labels.
        /// </summary>
        public static List<string>? BuildBizValue(IEnumerable<string>? values)
        {
            var result = values?.Where(v => !string.IsNullOrEmpty(v)).ToList();
            return result?.Count > 0 ? result : null;
        }

        /// <summary>
        /// Build BizValue directly from raw values.
        /// </summary>
        public static List<string>? BuildBizValue(params string[] values) =>
            BuildBizValue(values.AsEnumerable());

        /// <summary>
        /// Build serialized BizValue directly from raw values.
        /// </summary>
        public static string? BuildBizValueSerialized(IEnumerable<string>? values) =>
            BuildBizValue(values)?.SerializeAsStandardValue(StandardValueType.ListString);

        /// <summary>
        /// Build serialized BizValue directly from raw values.
        /// </summary>
        public static string? BuildBizValueSerialized(params string[] values) =>
            BuildBizValueSerialized(values.AsEnumerable());

        /// <summary>
        /// Match DbValues (IDs) to options and return BizValue (list of Choice.Labels).
        /// </summary>
        /// <param name="options">Choice options containing value-to-label mapping</param>
        /// <param name="dbValues">DbValues (IDs) to match</param>
        /// <returns>List of matched Choice.Labels, or null if none matched</returns>
        public static List<string>? MatchBizValue(
            MultipleChoicePropertyOptions? options,
            List<string>? dbValues)
        {
            if (options == null || dbValues == null || dbValues.Count == 0) return null;
            return PropertySystem.Property.ToBizValue(
                VirtualProperty(PropertyType.MultipleChoice, options), dbValues) as List<string>;
        }

        /// <summary>
        /// Match DbValues to options and return serialized BizValue.
        /// </summary>
        public static string? MatchBizValueSerialized(
            MultipleChoicePropertyOptions? options,
            List<string>? dbValues) =>
            MatchBizValue(options, dbValues)?.SerializeAsStandardValue(StandardValueType.ListString);
    }

    #endregion

    #region Complex Reference Types (Tags, Multilevel)

    /// <summary>
    /// Tags property value factory.
    /// DbValue = List of UUID/ID, BizValue = List of TagValue (group + name)
    /// </summary>
    public static class Tags
    {
        // ============================================================
        // DbValue - Build & Match
        // ============================================================

        /// <summary>
        /// Build DbValue directly from raw values (IDs/UUIDs).
        /// Use when you already have the IDs.
        /// </summary>
        public static List<string>? BuildDbValue(IEnumerable<string>? values)
        {
            var result = values?.Where(v => !string.IsNullOrEmpty(v)).ToList();
            return result?.Count > 0 ? result : null;
        }

        /// <summary>
        /// Build DbValue directly from raw values.
        /// </summary>
        public static List<string>? BuildDbValue(params string[] values) =>
            BuildDbValue(values.AsEnumerable());

        /// <summary>
        /// Build serialized DbValue directly from raw values.
        /// </summary>
        public static string? BuildDbValueSerialized(IEnumerable<string>? values) =>
            BuildDbValue(values)?.SerializeAsStandardValue(StandardValueType.ListString);

        /// <summary>
        /// Build serialized DbValue directly from raw values.
        /// </summary>
        public static string? BuildDbValueSerialized(params string[] values) =>
            BuildDbValueSerialized(values.AsEnumerable());

        /// <summary>
        /// Match TagValues to options and return DbValue (list of Tag.Value/IDs).
        /// </summary>
        /// <param name="options">Tags options containing tag-to-value mapping</param>
        /// <param name="tags">TagValues to match (group + name)</param>
        /// <param name="addOnMiss">If true, adds new tags to options when not found</param>
        /// <returns>List of matched Tag.Value (IDs), or null if none matched</returns>
        public static List<string>? MatchDbValue(
            TagsPropertyOptions? options,
            IEnumerable<TagValue>? tags,
            bool addOnMiss = false)
        {
            // Thin wrapper over the descriptor — the single implementation of matching.
            if (options == null || tags == null) return null;
            var property = VirtualProperty(PropertyType.Tags, options);
            var (dbValue, _) = PropertySystem.Property.ToDbValue(property, tags.ToList(), Policy(addOnMiss));
            return dbValue as List<string>;
        }

        /// <summary>
        /// Match TagValues to options and return serialized DbValue.
        /// </summary>
        public static string? MatchDbValueSerialized(
            TagsPropertyOptions? options,
            IEnumerable<TagValue>? tags,
            bool addOnMiss = false) =>
            MatchDbValue(options, tags, addOnMiss)?.SerializeAsStandardValue(StandardValueType.ListString);

        // ============================================================
        // BizValue - Build & Match
        // ============================================================

        /// <summary>
        /// Build BizValue directly from raw values (TagValues).
        /// Use when you already have the tag group/name pairs.
        /// </summary>
        public static List<TagValue>? BuildBizValue(IEnumerable<TagValue>? values)
        {
            var result = values?.Where(v => !string.IsNullOrEmpty(v.Name)).ToList();
            return result?.Count > 0 ? result : null;
        }

        /// <summary>
        /// Build BizValue directly from raw values.
        /// </summary>
        public static List<TagValue>? BuildBizValue(params TagValue[] values) =>
            BuildBizValue(values.AsEnumerable());

        /// <summary>
        /// Build serialized BizValue directly from raw values.
        /// </summary>
        public static string? BuildBizValueSerialized(IEnumerable<TagValue>? values) =>
            BuildBizValue(values)?.SerializeAsStandardValue(StandardValueType.ListTag);

        /// <summary>
        /// Build serialized BizValue directly from raw values.
        /// </summary>
        public static string? BuildBizValueSerialized(params TagValue[] values) =>
            BuildBizValueSerialized(values.AsEnumerable());

        /// <summary>
        /// Match DbValues (IDs) to options and return BizValue (list of TagValues).
        /// </summary>
        /// <param name="options">Tags options containing value-to-tag mapping</param>
        /// <param name="dbValues">DbValues (IDs) to match</param>
        /// <returns>List of matched TagValues (group + name), or null if none matched</returns>
        public static List<TagValue>? MatchBizValue(
            TagsPropertyOptions? options,
            List<string>? dbValues)
        {
            if (options == null || dbValues == null || dbValues.Count == 0) return null;
            return PropertySystem.Property.ToBizValue(
                VirtualProperty(PropertyType.Tags, options), dbValues) as List<TagValue>;
        }

        /// <summary>
        /// Match DbValues to options and return serialized BizValue.
        /// </summary>
        public static string? MatchBizValueSerialized(
            TagsPropertyOptions? options,
            List<string>? dbValues) =>
            MatchBizValue(options, dbValues)?.SerializeAsStandardValue(StandardValueType.ListTag);
    }

    /// <summary>
    /// Multilevel property value factory.
    /// DbValue = List of UUID/ID, BizValue = List of label chains (List&lt;List&lt;string&gt;&gt;)
    /// </summary>
    public static class Multilevel
    {
        // ============================================================
        // DbValue - Build & Match
        // ============================================================

        /// <summary>
        /// Build DbValue directly from raw values (IDs/UUIDs).
        /// Use when you already have the IDs.
        /// </summary>
        public static List<string>? BuildDbValue(IEnumerable<string>? values)
        {
            var result = values?.Where(v => !string.IsNullOrEmpty(v)).ToList();
            return result?.Count > 0 ? result : null;
        }

        /// <summary>
        /// Build DbValue directly from raw values.
        /// </summary>
        public static List<string>? BuildDbValue(params string[] values) =>
            BuildDbValue(values.AsEnumerable());

        /// <summary>
        /// Build serialized DbValue directly from raw values.
        /// </summary>
        public static string? BuildDbValueSerialized(IEnumerable<string>? values) =>
            BuildDbValue(values)?.SerializeAsStandardValue(StandardValueType.ListString);

        /// <summary>
        /// Build serialized DbValue directly from raw values.
        /// </summary>
        public static string? BuildDbValueSerialized(params string[] values) =>
            BuildDbValueSerialized(values.AsEnumerable());

        /// <summary>
        /// Match label chains to options and return DbValue (list of node IDs).
        /// </summary>
        /// <param name="options">Multilevel options containing label-chain-to-value mapping</param>
        /// <param name="paths">Label chains to match (each path is a list of labels from root to leaf)</param>
        /// <param name="addOnMiss">If true, adds new branches to options when paths not found</param>
        /// <returns>List of matched node IDs, or null if none matched</returns>
        public static List<string>? MatchDbValue(
            MultilevelPropertyOptions? options,
            IEnumerable<List<string>>? paths,
            bool addOnMiss = false)
        {
            // Thin wrapper over the descriptor — the single implementation of matching.
            if (options == null || paths == null) return null;
            var pathList = paths.Where(p => p.Count > 0).ToList();
            if (pathList.Count == 0) return null;

            var property = VirtualProperty(PropertyType.Multilevel, options);
            var (dbValue, _) = PropertySystem.Property.ToDbValue(property, pathList, Policy(addOnMiss));
            return dbValue as List<string>;
        }

        /// <summary>
        /// Match label chains to options and return serialized DbValue.
        /// </summary>
        public static string? MatchDbValueSerialized(
            MultilevelPropertyOptions? options,
            IEnumerable<List<string>>? paths,
            bool addOnMiss = false) =>
            MatchDbValue(options, paths, addOnMiss)?.SerializeAsStandardValue(StandardValueType.ListString);

        // ============================================================
        // BizValue - Build & Match
        // ============================================================

        /// <summary>
        /// Build BizValue directly from raw values (label chains).
        /// Use when you already have the label paths.
        /// </summary>
        public static List<List<string>>? BuildBizValue(IEnumerable<List<string>>? values)
        {
            var result = values?.Where(v => v.Count > 0).ToList();
            return result?.Count > 0 ? result : null;
        }

        /// <summary>
        /// Build BizValue directly from raw values.
        /// </summary>
        public static List<List<string>>? BuildBizValue(params List<string>[] values) =>
            BuildBizValue(values.AsEnumerable());

        /// <summary>
        /// Build serialized BizValue directly from raw values.
        /// </summary>
        public static string? BuildBizValueSerialized(IEnumerable<List<string>>? values) =>
            BuildBizValue(values)?.SerializeAsStandardValue(StandardValueType.ListListString);

        /// <summary>
        /// Build serialized BizValue directly from raw values.
        /// </summary>
        public static string? BuildBizValueSerialized(params List<string>[] values) =>
            BuildBizValueSerialized(values.AsEnumerable());

        /// <summary>
        /// Match DbValues (IDs) to options and return BizValue (list of label chains).
        /// </summary>
        /// <param name="options">Multilevel options containing value-to-label-chain mapping</param>
        /// <param name="dbValues">DbValues (IDs) to match</param>
        /// <returns>List of label chains (path from root to matched node), or null if none matched</returns>
        public static List<List<string>>? MatchBizValue(
            MultilevelPropertyOptions? options,
            List<string>? dbValues)
        {
            if (options == null || dbValues == null || dbValues.Count == 0) return null;
            return PropertySystem.Property.ToBizValue(
                VirtualProperty(PropertyType.Multilevel, options), dbValues) as List<List<string>>;
        }

        /// <summary>
        /// Match DbValues to options and return serialized BizValue.
        /// </summary>
        public static string? MatchBizValueSerialized(
            MultilevelPropertyOptions? options,
            List<string>? dbValues) =>
            MatchBizValue(options, dbValues)?.SerializeAsStandardValue(StandardValueType.ListListString);
    }

    #endregion
}
