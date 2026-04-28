using System;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.AI.Models.Db;

public record ChatConversationDbModel
{
    [Key]
    public int Id { get; set; }
    public string? Title { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsArchived { get; set; }
}
