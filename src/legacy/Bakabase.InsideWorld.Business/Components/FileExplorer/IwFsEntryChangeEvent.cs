using System;
using Bakabase.Abstractions.Extensions;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer
{
    public record IwFsEntryChangeEvent(IwFsEntryChangeType Type, string Path, string? PrevPath = null)
    {
        public string Path { get; set; } = Path.StandardizePath()!;
        public string? PrevPath { get; set; } = PrevPath.StandardizePath();
        public IwFsEntryChangeType Type { get; set; } = Type;
        public DateTime ChangedAt { get; set; } = DateTime.Now;
    }
}