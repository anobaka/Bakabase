using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db;

public record SourceMetadataMappingDbModel
{
    [Key]
    public int Id { get; set; }
    public ResourceSource Source { get; set; }
    public string MetadataField { get; set; } = null!;
    public int TargetPool { get; set; }
    public int TargetPropertyId { get; set; }
}
