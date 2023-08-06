using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.FileExplorer.Information;
using Bakabase.InsideWorld.Models.Extensions;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer
{
    public record IwFsEntryChangeEvent(IwFsEntryChangeType Type, string Path,
        string? PrevPath = null, IwFsTaskInfo? Task = null)
    {
        public string Path { get; set; } = Path.StandardizePath()!;
        public string? PrevPath { get; set; } = PrevPath.StandardizePath();
        public IwFsEntryChangeType Type { get; set; } = Type;
        public DateTime ChangedAt { get; set; } = DateTime.Now;
        public IwFsTaskInfo? Task { get; set; } = Task;
    }
}