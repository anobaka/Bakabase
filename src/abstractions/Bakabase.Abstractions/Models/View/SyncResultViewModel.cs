namespace Bakabase.Abstractions.Models.View;

public record SyncResultViewModel
{
    public int ResourceCount { get; set; }
    public int AddedResourceCount { get; set; }
    public int UpdatedResourceCount { get; set; }
    public int DirectoryResourceCount { get; set; }
    public int FileResourceCount { get; set; }
}