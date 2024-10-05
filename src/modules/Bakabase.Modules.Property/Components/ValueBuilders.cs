using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.Property.Components;

public record StringValueBuilder(string? Value) : IStandardValueBuilder<string>;
public record ListStringValueBuilder(List<string>? Value) : IStandardValueBuilder<List<string>>;
public record DecimalValueBuilder(decimal? Value): IStandardValueBuilder<decimal?>;
public record LinkValueBuilder(LinkValue? Value) : IStandardValueBuilder<LinkValue>;
public record BooleanValueBuilder(bool? Value): IStandardValueBuilder<bool?>;
public record DateTimeValueBuilder(DateTime? Value): IStandardValueBuilder<DateTime?>;
public record TimeValueBuilder(TimeSpan? Value): IStandardValueBuilder<TimeSpan?>;
public record ListListStringValueBuilder(List<List<string>>? Value) : IStandardValueBuilder<List<List<string>>>;
public record ListTagValueBuilder(List<TagValue>? Value) : IStandardValueBuilder<List<TagValue>>;