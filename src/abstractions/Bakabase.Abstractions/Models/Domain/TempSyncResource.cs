namespace Bakabase.Abstractions.Models.Domain;

public record TempSyncResource(string Path)
{
    public bool IsDirectory { get; set; }
    public Dictionary<Property, object?>? PropertyValues { get; set; }
    public List<TempSyncResource>? Children { get; set; }
    public TempSyncResource? Parent { get; set; }
    public DateTime FileCreatedAt { get; set; }
    public DateTime FileModifiedAt { get; set; }
}