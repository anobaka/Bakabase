using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Components;

/// <summary>
/// Type-safe factory for creating StandardValue instances.
/// Use this instead of raw object construction to ensure type safety.
/// </summary>
public static class StandardValueFactory
{
    /// <summary>
    /// Create a String StandardValue
    /// </summary>
    public static StandardValue<string> String(string? value) =>
        new(StandardValueType.String, value?.Trim());

    /// <summary>
    /// Create a ListString StandardValue
    /// </summary>
    public static StandardValue<List<string>> ListString(List<string>? value) =>
        new(StandardValueType.ListString, value?.Where(s => !string.IsNullOrEmpty(s)).ToList());

    /// <summary>
    /// Create a ListString StandardValue from params
    /// </summary>
    public static StandardValue<List<string>> ListString(params string[] values) =>
        ListString(values.ToList());

    /// <summary>
    /// Create a Decimal StandardValue
    /// </summary>
    public static StandardValue<decimal> Decimal(decimal value) =>
        new(StandardValueType.Decimal, value);

    /// <summary>
    /// Create a Decimal StandardValue from nullable
    /// </summary>
    public static StandardValue<decimal>? Decimal(decimal? value) =>
        value.HasValue ? new(StandardValueType.Decimal, value.Value) : null;

    /// <summary>
    /// Create a Link StandardValue
    /// </summary>
    public static StandardValue<LinkValue> Link(string? text, string? url) =>
        new(StandardValueType.Link, new LinkValue(text, url));

    /// <summary>
    /// Create a Link StandardValue from LinkValue
    /// </summary>
    public static StandardValue<LinkValue> Link(LinkValue? value) =>
        new(StandardValueType.Link, value);

    /// <summary>
    /// Create a Boolean StandardValue
    /// </summary>
    public static StandardValue<bool> Boolean(bool value) =>
        new(StandardValueType.Boolean, value);

    /// <summary>
    /// Create a Boolean StandardValue from nullable
    /// </summary>
    public static StandardValue<bool>? Boolean(bool? value) =>
        value.HasValue ? new(StandardValueType.Boolean, value.Value) : null;

    /// <summary>
    /// Create a DateTime StandardValue
    /// </summary>
    public static StandardValue<DateTime> DateTime(DateTime value) =>
        new(StandardValueType.DateTime, value);

    /// <summary>
    /// Create a DateTime StandardValue from nullable
    /// </summary>
    public static StandardValue<DateTime>? DateTime(DateTime? value) =>
        value.HasValue ? new(StandardValueType.DateTime, value.Value) : null;

    /// <summary>
    /// Create a Time StandardValue
    /// </summary>
    public static StandardValue<TimeSpan> Time(TimeSpan value) =>
        new(StandardValueType.Time, value);

    /// <summary>
    /// Create a Time StandardValue from nullable
    /// </summary>
    public static StandardValue<TimeSpan>? Time(TimeSpan? value) =>
        value.HasValue ? new(StandardValueType.Time, value.Value) : null;

    /// <summary>
    /// Create a ListListString StandardValue
    /// </summary>
    public static StandardValue<List<List<string>>> ListListString(List<List<string>>? value) =>
        new(StandardValueType.ListListString, value?.Where(l => l.Any()).ToList());

    /// <summary>
    /// Create a ListListString StandardValue from params
    /// </summary>
    public static StandardValue<List<List<string>>> ListListString(params List<string>[] values) =>
        ListListString(values.ToList());

    /// <summary>
    /// Create a ListTag StandardValue
    /// </summary>
    public static StandardValue<List<TagValue>> ListTag(List<TagValue>? value) =>
        new(StandardValueType.ListTag, value?.Where(t => !string.IsNullOrEmpty(t.Name)).ToList());

    /// <summary>
    /// Create a ListTag StandardValue from params
    /// </summary>
    public static StandardValue<List<TagValue>> ListTag(params TagValue[] values) =>
        ListTag(values.ToList());

    /// <summary>
    /// Create a ListTag StandardValue from group-name tuples
    /// </summary>
    public static StandardValue<List<TagValue>> ListTag(params (string? Group, string Name)[] values) =>
        ListTag(values.Select(v => new TagValue(v.Group, v.Name)).ToList());
}
