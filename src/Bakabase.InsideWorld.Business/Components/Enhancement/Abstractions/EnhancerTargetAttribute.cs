using System;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions;

public class EnhancerTargetAttribute(StandardValueType valueType) : Attribute
{
    public StandardValueType ValueType { get; } = valueType;
}