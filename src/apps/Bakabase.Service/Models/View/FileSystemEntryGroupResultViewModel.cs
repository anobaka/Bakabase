namespace Bakabase.Service.Models.View;

public record FileSystemEntryGroupResultViewModel
{
    public string RootPath { get; set; } = null!;
    public GroupViewModel[] Groups { get; set; } = [];
    public EntryViewModel[] UntouchedEntries { get; set; } = [];

    public record GroupViewModel
    {
        public string DirectoryName { get; set; } = null!;
        public EntryViewModel[] Entries { get; set; } = [];
        public string? ExistingFolderTarget { get; set; }
        public string? RenamedSourceName { get; set; }
    }

    public record EntryViewModel
    {
        public string Name { get; set; } = null!;
        public bool IsDirectory { get; set; }
        public MatchSpan[] MatchSpans { get; set; } = [];
    }

    public record MatchSpan
    {
        public int Start { get; set; }
        public int Length { get; set; }
    }
}
