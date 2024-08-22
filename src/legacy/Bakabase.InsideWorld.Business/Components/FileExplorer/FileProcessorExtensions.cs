using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer
{
    public static class FileProcessorExtensions
    {
        public static bool CanBeDecompressed(this IwFsType type)
        {
            return type switch
            {
                IwFsType.Unknown => true,
                IwFsType.Video => true,
                IwFsType.Audio => true,
                IwFsType.Image => true,
                IwFsType.CompressedFileEntry => true,
                IwFsType.CompressedFilePart => true,
                IwFsType.Directory => false,
                IwFsType.Symlink => false,
                IwFsType.Invalid => false,
                _ => false
            };
        }
    }
}