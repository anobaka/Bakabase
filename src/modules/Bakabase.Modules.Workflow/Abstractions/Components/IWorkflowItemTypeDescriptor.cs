namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// Describes one item type flowing through workflow chains. Activities and triggers refer
/// to types by their string tag (e.g. "item.exhentai.gallery"); a descriptor gives that tag
/// a display name plus the CLR shape used for serialization — which is what generic
/// transforms (the AI node especially) need to construct prompts and deserialize results.
/// </summary>
public interface IWorkflowItemTypeDescriptor
{
    /// <summary>The opaque string tag, e.g. "item.exhentai.gallery".</summary>
    string ItemType { get; }

    /// <summary>Human-readable name for the editor's type pickers.</summary>
    string DisplayName { get; }

    /// <summary>
    /// CLR type the item is represented as in memory. Used for JSON shape inference and
    /// strong-typed deserialization when a transform produces this type.
    /// </summary>
    System.Type ClrType { get; }
}
