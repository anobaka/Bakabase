using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.Property.Components.Accessors;

/// <summary>
/// Attachment property accessor.
/// For external access, use PropertySystem.Builtin and PropertyValueFactory.
/// </summary>
internal class AttachmentPropertyAccessor : BuiltinPropertyAccessor<List<string>>
{
    public AttachmentPropertyAccessor(ResourceProperty prop) : base(prop)
    {
    }

    // === DbValue ===
    public List<string>? BuildDbValue(IEnumerable<string>? paths) => PropertyValueFactory.Attachment.BuildDbValue(paths);
    public List<string>? BuildDbValue(params string[] paths) => PropertyValueFactory.Attachment.BuildDbValue(paths);
    public string? BuildDbValueSerialized(IEnumerable<string>? paths) => PropertyValueFactory.Attachment.BuildDbValueSerialized(paths);
    public string? BuildDbValueSerialized(params string[] paths) => PropertyValueFactory.Attachment.BuildDbValueSerialized(paths);

    // === BizValue ===
    public List<string>? BuildBizValue(List<string>? value) => PropertyValueFactory.Attachment.BuildBizValue(value);
    public string? BuildBizValueSerialized(List<string>? value) => PropertyValueFactory.Attachment.BuildBizValueSerialized(value);
}
