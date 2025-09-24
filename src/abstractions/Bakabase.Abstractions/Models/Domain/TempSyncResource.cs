namespace Bakabase.Abstractions.Models.Domain;

public record TempSyncResource(string Path)
{
    public int Id { get; set; }
    public bool IsFile { get; set; }
    public Dictionary<Property, object?>? PropertyValues { get; set; }
    public List<TempSyncResource>? Children { get; set; }
    public TempSyncResource? Parent { get; set; }
    public DateTime FileCreatedAt { get; set; }
    public DateTime FileModifiedAt { get; set; }

    public override string ToString()
    {
        if (Parent == null)
        {
            return Path;
        }

        return Path + " <- " + Parent;
    }
}