using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Components.BuiltinProperty;

namespace Bakabase.Modules.Property.Components.Accessors;

/// <summary>
/// Base accessor for built-in properties.
/// For external access, use PropertySystem.Builtin.
/// </summary>
internal abstract class BuiltinPropertyAccessor
{
    public ResourceProperty ResourceProperty { get; }
    public Bakabase.Abstractions.Models.Domain.Property Definition => BuiltinProperties.Get(ResourceProperty);
    public PropertyType Type => Definition.Type;
    public int Id => Definition.Id;
    public PropertyPool Pool => Definition.Pool;

    protected BuiltinPropertyAccessor(ResourceProperty resourceProperty)
    {
        ResourceProperty = resourceProperty;
    }
}

/// <summary>
/// Generic accessor with typed value.
/// For external access, use PropertySystem.Builtin.
/// </summary>
internal class BuiltinPropertyAccessor<TBizValue> : BuiltinPropertyAccessor
{
    public BuiltinPropertyAccessor(ResourceProperty prop) : base(prop)
    {
    }

    /// <summary>
    /// Get BizValue from DbValue
    /// </summary>
    public TBizValue? GetBizValue(object? dbValue) =>
        PropertySystem.Property.ToBizValue<TBizValue>(Definition, dbValue);
}
