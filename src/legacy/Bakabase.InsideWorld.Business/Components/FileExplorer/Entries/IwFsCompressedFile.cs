using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer.Entries
{
    public class IwFsCompressedFile : IwFsEntry
    {
        public IwFsCompressedFile()
        {
            Type = IwFsType.CompressedFileEntry;
        }

        public int Part { get; set; }
        public static string BuildDecompressionTaskName(string path) => $"Decompress: {path}";
    }
}
