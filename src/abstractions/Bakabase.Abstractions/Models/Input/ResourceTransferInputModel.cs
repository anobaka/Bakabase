namespace Bakabase.Abstractions.Models.Input;

public record ResourceTransferInputModel
{
    public List<Item> Items { get; set; } = [];
    public bool KeepMediaLibraryForAll { get; set; } = true;
    public bool DeleteAllSourceResources { get; set; }

    public record Item
    {
        public int FromId { get; set; }
        public int ToId { get; set; }
        public bool KeepMediaLibrary { get; set; } = true;
        public bool DeleteSourceResource { get; set; }
    }
}