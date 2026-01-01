using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.Property.Components.Accessors;

/// <summary>
/// DateTime property accessor.
/// For external access, use PropertySystem.Builtin and PropertyValueFactory.
/// </summary>
internal class DateTimePropertyAccessor : BuiltinPropertyAccessor<System.DateTime>
{
    public DateTimePropertyAccessor(ResourceProperty prop) : base(prop)
    {
    }

    // === DbValue ===
    public System.DateTime? BuildDbValue(System.DateTime? value) => PropertyValueFactory.DateTime.BuildDbValue(value);
    public string? BuildDbValueSerialized(System.DateTime? value) => PropertyValueFactory.DateTime.BuildDbValueSerialized(value);

    // === BizValue ===
    public System.DateTime? BuildBizValue(System.DateTime? value) => PropertyValueFactory.DateTime.BuildBizValue(value);
    public string? BuildBizValueSerialized(System.DateTime? value) => PropertyValueFactory.DateTime.BuildBizValueSerialized(value);
}
