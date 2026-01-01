using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.Property.Components;

/// <summary>
/// Type-safe factory for creating Property values.
///
/// API Pattern:
/// - BuildDbValue/BuildBizValue: Direct construction (when you have raw values)
/// - MatchDbValue/MatchBizValue: Match through options (for reference types like Choice)
/// - *Serialized: Returns serialized string version
/// </summary>
public static class PropertyValueFactory
{
    #region Text Types (SingleLineText, MultilineText, Formula)

    /// <summary>
    /// SingleLineText property value factory
    /// </summary>
    public static class SingleLineText
    {
        // === DbValue ===
        public static string? BuildDbValue(string? value) => value?.Trim();
        public static string? BuildDbValueSerialized(string? value) =>
            BuildDbValue(value)?.SerializeAsStandardValue(StandardValueType.String);

        // === BizValue ===
        public static string? BuildBizValue(string? value) => value?.Trim();
        public static string? BuildBizValueSerialized(string? value) =>
            BuildBizValue(value)?.SerializeAsStandardValue(StandardValueType.String);

        // Legacy compatibility
        [Obsolete("Use BuildDbValue instead")]
        public static string? Db(string? value) => BuildDbValue(value);
        [Obsolete("Use BuildBizValue instead")]
        public static string? Biz(string? dbValue) => BuildBizValue(dbValue);
    }

    /// <summary>
    /// MultilineText property value factory
    /// </summary>
    public static class MultilineText
    {
        // === DbValue ===
        public static string? BuildDbValue(string? value) => value?.Trim();
        public static string? BuildDbValueSerialized(string? value) =>
            BuildDbValue(value)?.SerializeAsStandardValue(StandardValueType.String);

        // === BizValue ===
        public static string? BuildBizValue(string? value) => value?.Trim();
        public static string? BuildBizValueSerialized(string? value) =>
            BuildBizValue(value)?.SerializeAsStandardValue(StandardValueType.String);

        // Legacy compatibility
        [Obsolete("Use BuildDbValue instead")]
        public static string? Db(string? value) => BuildDbValue(value);
        [Obsolete("Use BuildBizValue instead")]
        public static string? Biz(string? dbValue) => BuildBizValue(dbValue);
    }

    /// <summary>
    /// Formula property value factory
    /// </summary>
    public static class Formula
    {
        // === DbValue ===
        public static string? BuildDbValue(string? value) => value?.Trim();
        public static string? BuildDbValueSerialized(string? value) =>
            BuildDbValue(value)?.SerializeAsStandardValue(StandardValueType.String);

        // === BizValue ===
        public static string? BuildBizValue(string? value) => value?.Trim();
        public static string? BuildBizValueSerialized(string? value) =>
            BuildBizValue(value)?.SerializeAsStandardValue(StandardValueType.String);

        // Legacy compatibility
        [Obsolete("Use BuildDbValue instead")]
        public static string? Db(string? value) => BuildDbValue(value);
        [Obsolete("Use BuildBizValue instead")]
        public static string? Biz(string? dbValue) => BuildBizValue(dbValue);
    }

    #endregion

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
            if (string.IsNullOrEmpty(label)) return null;

            var existing = options?.Choices?.FirstOrDefault(c => c.Label == label);
            if (existing != null)
            {
                return existing.Value;
            }

            if (addOnMiss && options != null)
            {
                options.AddChoices(true, [label], null);
                return options.Choices?.FirstOrDefault(c => c.Label == label)?.Value;
            }

            return null;
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
            if (string.IsNullOrEmpty(dbValue)) return null;
            return options?.Choices?.FirstOrDefault(c => c.Value == dbValue)?.Label;
        }

        /// <summary>
        /// Match DbValue to options and return serialized BizValue.
        /// </summary>
        public static string? MatchBizValueSerialized(
            SingleChoicePropertyOptions? options,
            string? dbValue) =>
            MatchBizValue(options, dbValue)?.SerializeAsStandardValue(StandardValueType.String);

        // Legacy compatibility
        [Obsolete("Use MatchDbValue instead")]
        public static string? Db(SingleChoicePropertyOptions? options, string? label) =>
            MatchDbValue(options, label);

        [Obsolete("Use MatchDbValue with addOnMiss: true instead")]
        public static (string? DbValue, bool OptionsChanged) DbWithAutoCreate(
            SingleChoicePropertyOptions options, string? label)
        {
            if (string.IsNullOrEmpty(label)) return (null, false);
            var existingCount = options.Choices?.Count ?? 0;
            var result = MatchDbValue(options, label, addOnMiss: true);
            var changed = (options.Choices?.Count ?? 0) > existingCount;
            return (result, changed);
        }

        [Obsolete("Use MatchBizValue instead")]
        public static string? Biz(SingleChoicePropertyOptions? options, string? dbValue) =>
            MatchBizValue(options, dbValue);
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
            if (labels == null) return null;

            var labelsArray = labels.Where(l => !string.IsNullOrEmpty(l)).ToArray();
            if (labelsArray.Length == 0) return null;

            if (addOnMiss && options != null)
            {
                options.AddChoices(true, labelsArray, null);
            }

            var result = labelsArray
                .Select(l => options?.Choices?.FirstOrDefault(c => c.Label == l)?.Value)
                .OfType<string>()
                .ToList();

            return result.Count > 0 ? result : null;
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
            if (dbValues == null || dbValues.Count == 0) return null;
            var result = dbValues
                .Select(v => options?.Choices?.FirstOrDefault(c => c.Value == v)?.Label)
                .OfType<string>()
                .ToList();
            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// Match DbValues to options and return serialized BizValue.
        /// </summary>
        public static string? MatchBizValueSerialized(
            MultipleChoicePropertyOptions? options,
            List<string>? dbValues) =>
            MatchBizValue(options, dbValues)?.SerializeAsStandardValue(StandardValueType.ListString);

        // Legacy compatibility
        [Obsolete("Use MatchDbValue instead")]
        public static List<string>? Db(MultipleChoicePropertyOptions? options, IEnumerable<string>? labels) =>
            MatchDbValue(options, labels);

        [Obsolete("Use MatchDbValue with addOnMiss: true instead")]
        public static (List<string>? DbValue, bool OptionsChanged) DbWithAutoCreate(
            MultipleChoicePropertyOptions options, IEnumerable<string>? labels)
        {
            if (labels == null) return (null, false);
            var existingCount = options.Choices?.Count ?? 0;
            var result = MatchDbValue(options, labels, addOnMiss: true);
            var changed = (options.Choices?.Count ?? 0) > existingCount;
            return (result, changed);
        }

        [Obsolete("Use MatchBizValue instead")]
        public static List<string>? Biz(MultipleChoicePropertyOptions? options, List<string>? dbValues) =>
            MatchBizValue(options, dbValues);
    }

    #endregion

    #region Numeric Types (Number, Percentage, Rating)

    /// <summary>
    /// Number property value factory
    /// </summary>
    public static class Number
    {
        // === DbValue ===
        public static decimal? BuildDbValue(decimal? value) => value;
        public static string? BuildDbValueSerialized(decimal? value) =>
            value?.SerializeAsStandardValue(StandardValueType.Decimal);

        // === BizValue ===
        public static decimal? BuildBizValue(decimal? value) => value;
        public static string? BuildBizValueSerialized(decimal? value) =>
            value?.SerializeAsStandardValue(StandardValueType.Decimal);

        // Legacy compatibility
        [Obsolete("Use BuildDbValue instead")]
        public static decimal? Db(decimal? value) => BuildDbValue(value);
        [Obsolete("Use BuildBizValue instead")]
        public static decimal? Biz(decimal? dbValue) => BuildBizValue(dbValue);
    }

    /// <summary>
    /// Percentage property value factory
    /// </summary>
    public static class Percentage
    {
        // === DbValue ===
        public static decimal? BuildDbValue(decimal? value) => value;
        public static string? BuildDbValueSerialized(decimal? value) =>
            value?.SerializeAsStandardValue(StandardValueType.Decimal);

        // === BizValue ===
        public static decimal? BuildBizValue(decimal? value) => value;
        public static string? BuildBizValueSerialized(decimal? value) =>
            value?.SerializeAsStandardValue(StandardValueType.Decimal);

        // Legacy compatibility
        [Obsolete("Use BuildDbValue instead")]
        public static decimal? Db(decimal? value) => BuildDbValue(value);
        [Obsolete("Use BuildBizValue instead")]
        public static decimal? Biz(decimal? dbValue) => BuildBizValue(dbValue);
    }

    /// <summary>
    /// Rating property value factory
    /// </summary>
    public static class Rating
    {
        // === DbValue ===
        public static decimal? BuildDbValue(decimal? value) => value;
        public static string? BuildDbValueSerialized(decimal? value) =>
            value?.SerializeAsStandardValue(StandardValueType.Decimal);

        // === BizValue ===
        public static decimal? BuildBizValue(decimal? value) => value;
        public static string? BuildBizValueSerialized(decimal? value) =>
            value?.SerializeAsStandardValue(StandardValueType.Decimal);

        // Legacy compatibility
        [Obsolete("Use BuildDbValue instead")]
        public static decimal? Db(decimal? value) => BuildDbValue(value);
        [Obsolete("Use BuildBizValue instead")]
        public static decimal? Biz(decimal? dbValue) => BuildBizValue(dbValue);
    }

    #endregion

    #region Other Types

    /// <summary>
    /// Boolean property value factory
    /// </summary>
    public static class Boolean
    {
        // === DbValue ===
        public static bool? BuildDbValue(bool? value) => value;
        public static string? BuildDbValueSerialized(bool? value) =>
            value?.SerializeAsStandardValue(StandardValueType.Boolean);

        // === BizValue ===
        public static bool? BuildBizValue(bool? value) => value;
        public static string? BuildBizValueSerialized(bool? value) =>
            value?.SerializeAsStandardValue(StandardValueType.Boolean);

        // Legacy compatibility
        [Obsolete("Use BuildDbValue instead")]
        public static bool? Db(bool? value) => BuildDbValue(value);
        [Obsolete("Use BuildBizValue instead")]
        public static bool? Biz(bool? dbValue) => BuildBizValue(dbValue);
    }

    /// <summary>
    /// Link property value factory
    /// </summary>
    public static class Link
    {
        // === DbValue ===
        public static LinkValue? BuildDbValue(string? text, string? url)
        {
            var lv = new LinkValue(text, url);
            return lv.IsEmpty ? null : lv;
        }

        public static LinkValue? BuildDbValue(LinkValue? value) =>
            value?.IsEmpty == true ? null : value;

        public static string? BuildDbValueSerialized(string? text, string? url) =>
            BuildDbValue(text, url)?.SerializeAsStandardValue(StandardValueType.Link);

        public static string? BuildDbValueSerialized(LinkValue? value) =>
            BuildDbValue(value)?.SerializeAsStandardValue(StandardValueType.Link);

        // === BizValue ===
        public static LinkValue? BuildBizValue(LinkValue? value) => value;

        public static string? BuildBizValueSerialized(LinkValue? value) =>
            value?.SerializeAsStandardValue(StandardValueType.Link);

        // Legacy compatibility
        [Obsolete("Use BuildDbValue instead")]
        public static LinkValue? Db(string? text, string? url) => BuildDbValue(text, url);
        [Obsolete("Use BuildDbValue instead")]
        public static LinkValue? Db(LinkValue? value) => BuildDbValue(value);
        [Obsolete("Use BuildBizValue instead")]
        public static LinkValue? Biz(LinkValue? dbValue) => BuildBizValue(dbValue);
    }

    /// <summary>
    /// Attachment property value factory
    /// </summary>
    public static class Attachment
    {
        // === DbValue ===
        public static List<string>? BuildDbValue(IEnumerable<string>? paths)
        {
            var result = paths?.Where(p => !string.IsNullOrEmpty(p)).ToList();
            return result?.Count > 0 ? result : null;
        }

        public static List<string>? BuildDbValue(params string[] paths) =>
            BuildDbValue(paths.AsEnumerable());

        public static string? BuildDbValueSerialized(IEnumerable<string>? paths) =>
            BuildDbValue(paths)?.SerializeAsStandardValue(StandardValueType.ListString);

        public static string? BuildDbValueSerialized(params string[] paths) =>
            BuildDbValueSerialized(paths.AsEnumerable());

        // === BizValue ===
        public static List<string>? BuildBizValue(List<string>? value) => value;

        public static string? BuildBizValueSerialized(List<string>? value) =>
            value?.SerializeAsStandardValue(StandardValueType.ListString);

        // Legacy compatibility
        [Obsolete("Use BuildDbValue instead")]
        public static List<string>? Db(IEnumerable<string>? paths) => BuildDbValue(paths);
        [Obsolete("Use BuildBizValue instead")]
        public static List<string>? Biz(List<string>? dbValue) => BuildBizValue(dbValue);
    }

    /// <summary>
    /// Date property value factory
    /// </summary>
    public static class Date
    {
        // === DbValue ===
        public static System.DateTime? BuildDbValue(System.DateTime? value) => value;
        public static string? BuildDbValueSerialized(System.DateTime? value) =>
            value?.SerializeAsStandardValue(StandardValueType.DateTime);

        // === BizValue ===
        public static System.DateTime? BuildBizValue(System.DateTime? value) => value;
        public static string? BuildBizValueSerialized(System.DateTime? value) =>
            value?.SerializeAsStandardValue(StandardValueType.DateTime);

        // Legacy compatibility
        [Obsolete("Use BuildDbValue instead")]
        public static System.DateTime? Db(System.DateTime? value) => BuildDbValue(value);
        [Obsolete("Use BuildBizValue instead")]
        public static System.DateTime? Biz(System.DateTime? dbValue) => BuildBizValue(dbValue);
    }

    /// <summary>
    /// DateTime property value factory
    /// </summary>
    public static class DateTime
    {
        // === DbValue ===
        public static System.DateTime? BuildDbValue(System.DateTime? value) => value;
        public static string? BuildDbValueSerialized(System.DateTime? value) =>
            value?.SerializeAsStandardValue(StandardValueType.DateTime);

        // === BizValue ===
        public static System.DateTime? BuildBizValue(System.DateTime? value) => value;
        public static string? BuildBizValueSerialized(System.DateTime? value) =>
            value?.SerializeAsStandardValue(StandardValueType.DateTime);

        // Legacy compatibility
        [Obsolete("Use BuildDbValue instead")]
        public static System.DateTime? Db(System.DateTime? value) => BuildDbValue(value);
        [Obsolete("Use BuildBizValue instead")]
        public static System.DateTime? Biz(System.DateTime? dbValue) => BuildBizValue(dbValue);
    }

    /// <summary>
    /// Time property value factory
    /// </summary>
    public static class Time
    {
        // === DbValue ===
        public static TimeSpan? BuildDbValue(TimeSpan? value) => value;
        public static string? BuildDbValueSerialized(TimeSpan? value) =>
            value?.SerializeAsStandardValue(StandardValueType.Time);

        // === BizValue ===
        public static TimeSpan? BuildBizValue(TimeSpan? value) => value;
        public static string? BuildBizValueSerialized(TimeSpan? value) =>
            value?.SerializeAsStandardValue(StandardValueType.Time);

        // Legacy compatibility
        [Obsolete("Use BuildDbValue instead")]
        public static TimeSpan? Db(TimeSpan? value) => BuildDbValue(value);
        [Obsolete("Use BuildBizValue instead")]
        public static TimeSpan? Biz(TimeSpan? dbValue) => BuildBizValue(dbValue);
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
            if (tags == null) return null;

            var tagList = tags.Where(t => !string.IsNullOrEmpty(t.Name)).ToList();
            if (tagList.Count == 0) return null;

            if (addOnMiss && options != null)
            {
                options.Tags ??= [];
                foreach (var tag in tagList)
                {
                    var existing = options.Tags.FirstOrDefault(x => x.Name == tag.Name && x.Group == tag.Group);
                    if (existing == null)
                    {
                        options.Tags.Add(new TagsPropertyOptions.TagOptions(tag.Group, tag.Name)
                        {
                            Value = TagsPropertyOptions.TagOptions.GenerateValue()
                        });
                    }
                }
            }

            var result = tagList
                .Select(t => options?.Tags?.FirstOrDefault(x => x.Name == t.Name && x.Group == t.Group)?.Value)
                .OfType<string>()
                .ToList();

            return result.Count > 0 ? result : null;
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
            if (dbValues == null || dbValues.Count == 0) return null;

            var result = dbValues
                .Select(v => options?.Tags?.FirstOrDefault(x => x.Value == v)?.ToTagValue())
                .OfType<TagValue>()
                .ToList();

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// Match DbValues to options and return serialized BizValue.
        /// </summary>
        public static string? MatchBizValueSerialized(
            TagsPropertyOptions? options,
            List<string>? dbValues) =>
            MatchBizValue(options, dbValues)?.SerializeAsStandardValue(StandardValueType.ListTag);

        // Legacy compatibility
        [Obsolete("Use MatchDbValue instead")]
        public static List<string>? Db(TagsPropertyOptions? options, IEnumerable<TagValue>? tags) =>
            MatchDbValue(options, tags);

        [Obsolete("Use MatchDbValue with addOnMiss: true instead")]
        public static (List<string>? DbValue, bool OptionsChanged) DbWithAutoCreate(
            TagsPropertyOptions options, IEnumerable<TagValue>? tags)
        {
            if (tags == null) return (null, false);
            var existingCount = options.Tags?.Count ?? 0;
            var result = MatchDbValue(options, tags, addOnMiss: true);
            var changed = (options.Tags?.Count ?? 0) > existingCount;
            return (result, changed);
        }

        [Obsolete("Use MatchBizValue instead")]
        public static List<TagValue>? Biz(TagsPropertyOptions? options, List<string>? dbValues) =>
            MatchBizValue(options, dbValues);
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
            if (paths == null || options?.Data == null && !addOnMiss) return null;

            var pathList = paths.Where(p => p.Count > 0).ToList();
            if (pathList.Count == 0) return null;

            if (addOnMiss && options != null)
            {
                options.AddBranchOptions(pathList);
            }

            var result = options?.Data?.FindValuesByLabelChains(pathList)
                .OfType<string>()
                .ToList();

            return result?.Count > 0 ? result : null;
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
            if (dbValues == null || dbValues.Count == 0 || options?.Data == null) return null;

            var result = new List<List<string>>();
            foreach (var v in dbValues)
            {
                foreach (var d in options.Data)
                {
                    var chain = d.FindLabelChain(v);
                    if (chain != null)
                    {
                        result.Add(chain.ToList());
                    }
                }
            }

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// Match DbValues to options and return serialized BizValue.
        /// </summary>
        public static string? MatchBizValueSerialized(
            MultilevelPropertyOptions? options,
            List<string>? dbValues) =>
            MatchBizValue(options, dbValues)?.SerializeAsStandardValue(StandardValueType.ListListString);

        // Legacy compatibility
        [Obsolete("Use MatchDbValue instead")]
        public static List<string>? Db(MultilevelPropertyOptions? options, IEnumerable<List<string>>? paths) =>
            MatchDbValue(options, paths);

        [Obsolete("Use MatchDbValue with addOnMiss: true instead")]
        public static (List<string>? DbValue, bool OptionsChanged) DbWithAutoCreate(
            MultilevelPropertyOptions options, IEnumerable<List<string>>? paths)
        {
            if (paths == null) return (null, false);
            var existingNodeCount = CountNodes(options.Data);
            var result = MatchDbValue(options, paths, addOnMiss: true);
            var changed = CountNodes(options.Data) > existingNodeCount;
            return (result, changed);
        }

        private static int CountNodes(List<MultilevelDataOptions>? data)
        {
            if (data == null) return 0;
            return data.Sum(d => 1 + CountNodes(d.Children));
        }

        [Obsolete("Use MatchBizValue instead")]
        public static List<List<string>>? Biz(MultilevelPropertyOptions? options, List<string>? dbValues) =>
            MatchBizValue(options, dbValues);
    }

    #endregion
}
