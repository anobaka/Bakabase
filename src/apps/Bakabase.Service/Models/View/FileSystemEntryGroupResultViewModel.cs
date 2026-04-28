using System.Collections.Generic;

namespace Bakabase.Service.Models.View;

public record FileSystemEntryGroupResultViewModel
{
    public string RootPath { get; set; } = null!;
    public GroupViewModel[] Groups { get; set; } = [];

    public record GroupViewModel
    {
        public string DirectoryName { get; set; } = null!;
        public string[] Filenames { get; set; } = [];
    }
}