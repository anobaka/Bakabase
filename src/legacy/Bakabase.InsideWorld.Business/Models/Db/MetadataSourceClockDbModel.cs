using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Business.Models.Db;

// Local protocol state. Never copied into another device's library or definition sync.
public sealed class MetadataSourceClockDbModel
{
    [Key] public int Id { get; set; }
    public string Generation { get; set; } = string.Empty;
    public long Revision { get; set; }
}

public sealed class MetadataSourceChangeDbModel
{
    [Key] public int ResourceId { get; set; }
    public long Revision { get; set; }
}
