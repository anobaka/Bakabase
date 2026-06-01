namespace Bakabase.Modules.Workflow.Abstractions.Models.View;

public record WorkflowItemTypeDescriptorViewModel
{
    public string ItemType { get; set; } = null!;
    public string DisplayName { get; set; } = null!;

    /// <summary>
    /// Auto-generated from <c>ClrType</c> via reflection so the editor's "type pill" can
    /// show a field list (and the AI activity has shape info for prompts). Each entry is
    /// one public read/write or init-only property.
    /// </summary>
    public List<WorkflowItemTypeFieldViewModel> Fields { get; set; } = [];
}

public record WorkflowItemTypeFieldViewModel
{
    public string Name { get; set; } = null!;
    /// <summary>Friendly CLR type name (e.g. "string", "int", "string[]").</summary>
    public string Type { get; set; } = null!;
    public bool Nullable { get; set; }
}
