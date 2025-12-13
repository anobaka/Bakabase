using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.Property.Components.Accessors;

/// <summary>
/// Text property accessor (SingleLineText, MultilineText).
/// For external access, use PropertySystem.Builtin and PropertyValueFactory.
/// </summary>
internal class TextPropertyAccessor : BuiltinPropertyAccessor<string>
{
    public TextPropertyAccessor(ResourceProperty prop) : base(prop)
    {
    }

    // === DbValue ===
    public string? BuildDbValue(string? value) => PropertyValueFactory.SingleLineText.BuildDbValue(value);
    public string? BuildDbValueSerialized(string? value) => PropertyValueFactory.SingleLineText.BuildDbValueSerialized(value);

    // === BizValue ===
    public string? BuildBizValue(string? value) => PropertyValueFactory.SingleLineText.BuildBizValue(value);
    public string? BuildBizValueSerialized(string? value) => PropertyValueFactory.SingleLineText.BuildBizValueSerialized(value);
}
