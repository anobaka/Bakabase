using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer
{
    public enum IwFsType
    {
        Unknown = 0,
        Directory = 100,
        Image = 200,
        /// <summary>
        /// File or entry
        /// </summary>
        CompressedFileEntry = 300,
        CompressedFilePart = 400,
        Symlink = 500,
        Video = 600,
        Audio = 700,
        Invalid = 10000
    }
}
