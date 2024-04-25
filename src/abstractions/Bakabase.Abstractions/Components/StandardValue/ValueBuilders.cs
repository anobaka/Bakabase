using System.Data.SqlTypes;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Components.StandardValue;

public record TextValueBuilder(string? Value) : IStandardValueBuilder<string>;
public record MultipleTextValueBuilder(List<string>? Value) : IStandardValueBuilder<List<string>>;
public record DecimalValueBuilder(decimal? Value): IStandardValueBuilder<decimal?>;
public record LinkValueBuilder(LinkData? Value) : IStandardValueBuilder<LinkData>;
public record BooleanValueBuilder(bool? Value): IStandardValueBuilder<bool?>;
public record DateTimeValueBuilder(DateTime? Value): IStandardValueBuilder<DateTime?>;
public record TimeValueBuilder(TimeSpan? Value): IStandardValueBuilder<TimeSpan?>;
public record MultipleTextTreeValueBuilder(List<List<string>>? Value) : IStandardValueBuilder<List<List<string>>>;