using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.FileExplorer.Entries;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer
{
    public class IwFsPreview
    {
        public IwFsEntry[] Entries { get; set; } = [];
        public IwFsCompressedFileGroup[] CompressedFileGroups { get; set; } = [];
    }
}