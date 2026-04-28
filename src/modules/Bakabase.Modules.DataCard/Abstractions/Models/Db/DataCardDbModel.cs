using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.DataCard.Abstractions.Models.Db;

public record DataCardDbModel
{
    [Key] public int Id { get; set; }
    public int TypeId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
