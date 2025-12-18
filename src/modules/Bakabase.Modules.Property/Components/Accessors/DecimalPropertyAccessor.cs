using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.Property.Components.Accessors;

/// <summary>
/// Decimal property accessor (Number, Percentage, Rating).
/// For external access, use PropertySystem.Builtin and PropertyValueFactory.
/// </summary>
internal class DecimalPropertyAccessor : BuiltinPropertyAccessor<decimal>
{
    public DecimalPropertyAccessor(ResourceProperty prop) : base(prop)
    {
    }

    // === DbValue ===
    public decimal? BuildDbValue(decimal? value) => PropertyValueFactory.Number.BuildDbValue(value);
    public string? BuildDbValueSerialized(decimal? value) => PropertyValueFactory.Number.BuildDbValueSerialized(value);

    // === BizValue ===
    public decimal? BuildBizValue(decimal? value) => PropertyValueFactory.Number.BuildBizValue(value);
    public string? BuildBizValueSerialized(decimal? value) => PropertyValueFactory.Number.BuildBizValueSerialized(value);
}
