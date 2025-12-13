namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Records resources created by a Resource mark.
/// </summary>
public class ResourceMarkEffect
{
    public int Id { get; set; }
    public int MarkId { get; set; }
    public string Path { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
