using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components;

public record StringValueBuilder(string? Value) : IStandardValueBuilder<string>;
public record ListStringValueBuilder(List<string>? Value) : IStandardValueBuilder<List<string>>;
public record DecimalValueBuilder(decimal? Value): IStandardValueBuilder<decimal?>;
public record LinkValueBuilder(LinkValue? Value) : IStandardValueBuilder<LinkValue>;
public record BooleanValueBuilder(bool? Value): IStandardValueBuilder<bool?>;
public record DateTimeValueBuilder(DateTime? Value): IStandardValueBuilder<DateTime?>;
public record TimeValueBuilder(TimeSpan? Value): IStandardValueBuilder<TimeSpan?>;
public record ListListStringValueBuilder(List<List<string>>? Value) : IStandardValueBuilder<List<List<string>>>;