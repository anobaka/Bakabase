using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Collection.Models.Input;

public record CollectionInputModel
{
    [Required] [MaxLength(128)] public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? Color { get; set; }

    public string? CoverPath { get; set; }

    /// <summary>A serialized resource search; everything it matches is a member. Null for no rule.</summary>
    public string? RuleSearchJson { get; set; }

    public bool AutoAcquire { get; set; }

    public string? AcquisitionSettingsJson { get; set; }

    public int Order { get; set; }
}

public record CollectionMembersInputModel
{
    [Required] public List<int> ResourceIds { get; set; } = [];
}
