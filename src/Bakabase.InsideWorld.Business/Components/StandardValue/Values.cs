using System;
using System.Collections.Generic;
using Bakabase.Abstractions.Components.StandardValue;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.StandardValue;

public record TextValueBuilder(string Value) : IStandardValueBuilder<string>;
public record MultipleTextValue(List<string> Value) : IStandardValueBuilder<List<string>>;
public record DecimalValue(decimal Value): IStandardValueBuilder<decimal>;
public record LinkValue(LinkData Value) : IStandardValueBuilder<LinkData>;
public record BooleanValue(bool Value): IStandardValueBuilder<bool>;
public record DateTimeValue(DateTime Value): IStandardValueBuilder<DateTime>;
public record TimeValue(TimeSpan Value): IStandardValueBuilder<TimeSpan>;
public record MultipleTextTreeValue(List<List<string>> Value) : IStandardValueBuilder<List<List<string>>>;