using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Db;

public record EnhancementRecord
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public int EnhancerId { get; set; }
    public DateTime EnhancedAt { get; set; }
}