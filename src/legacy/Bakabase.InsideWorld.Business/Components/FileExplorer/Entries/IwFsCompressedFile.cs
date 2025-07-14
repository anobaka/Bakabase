using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer.Entries
{
    public class IwFsCompressedFile(string path, IwFsType type = IwFsType.Unknown) : IwFsEntry(path, type)
    {
        public int Part { get; set; }
        public static string BuildDecompressionTaskName(string path) => $"Decompress: {path}";
    }
}