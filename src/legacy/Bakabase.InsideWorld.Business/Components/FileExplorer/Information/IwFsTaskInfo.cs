using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer.Information
{
    public class IwFsTaskInfo
    {
        public string Path { get; set; }
        public IwFsEntryTaskType Type { get; set; }
        public int Percentage { get; set; }
        public string? Error { get; set; }
        public string BackgroundTaskId { get; set; }
        public string Name { get; set; }

        public IwFsTaskInfo(string path, IwFsEntryTaskType type, string backgroundTaskId, string name = null)
        {
            Path = path;
            Type = type;
            BackgroundTaskId = backgroundTaskId;
            Name = name ?? Type.ToString();
        }
    }
}