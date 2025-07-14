using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Extensions;
using NPOI.SS.Formula.Functions;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer
{
    public class IwFsEntry
    {
        public IwFsEntry(string path, IwFsType type = IwFsType.Unknown)
        {
            Path = path.StandardizePath()!;
            Name = System.IO.Path.GetFileName(path);

            if (string.IsNullOrEmpty(Name))
            {
                Name = Path;
            }

            var ext = type == IwFsType.Unknown ? System.IO.Path.GetExtension(Name) : null;

            if (type == IwFsType.Unknown)
            {
                if (InternalOptions.ImageExtensions.Contains(ext))
                {
                    type = IwFsType.Image;
                }
                else
                {
                    if (InternalOptions.VideoExtensions.Contains(ext))
                    {
                        type = IwFsType.Video;
                    }
                    else
                    {
                        if (InternalOptions.AudioExtensions.Contains(ext))
                        {
                            type = IwFsType.Audio;
                        }
                    }
                }
            }

            Ext = type == IwFsType.Unknown ? System.IO.Path.GetExtension(Name) : null;
            Type = type;
            MeaningfulName = System.IO.Path.GetFileNameWithoutExtension(Name);
            // CreationTime = fileSystemInfo?.CreationTime;
            // LastWriteTime = fileSystemInfo?.LastWriteTime;
            // Attributes = attrs.ToArray();
            // ChildrenCount = fileSystemInfo is FileInfo ? 0 : null;
            // FileCount = fileSystemInfo is FileInfo ? 0 : null;
            // DirectoryCount = fileSystemInfo is FileInfo ? 0 : null;

            if (Type.CanBeDecompressed())
            {
                PasswordsForDecompressing = path.GetPasswordsFromPath();
            }
        }

        public string Path { get; set; }
        public string Name { get; set; }
        public string MeaningfulName { get; set; }
        public string? Ext { get; set; }
        // public IwFsAttribute[] Attributes { get; set; }
        public IwFsType Type { get; set; }
        // public long? Size { get; set; }

        // public int? ChildrenCount { get; set; }

        // public int? FileCount { get; set; }
        // public int? DirectoryCount { get; set; }
        // public DateTime? CreationTime { get; set; }
        // public DateTime? LastWriteTime { get; set; }
        public string[] PasswordsForDecompressing { get; set; } = [];

        public override string ToString()
        {
            return Path;
        }
    }
}