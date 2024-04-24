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
        public IwFsEntry()
        {
        }

        public IwFsEntry(string path)
        {
            Path = path.StandardizePath()!;
            Name = System.IO.Path.GetFileName(path);

            if (string.IsNullOrEmpty(Name))
            {
                Name = Path;
            }

            FileSystemInfo? fileSystemInfo = null;
            var type = IwFsType.Unknown;
            if (Directory.Exists(path))
            {
                fileSystemInfo = new DirectoryInfo(path);
                type = IwFsType.Directory;
            }
            else
            {
                if (File.Exists(path))
                {
                    fileSystemInfo = new FileInfo(path);
                }
                else
                {
                    type = IwFsType.Invalid;
                }
            }

            if (fileSystemInfo != null)
            {
                if (fileSystemInfo.Name.StandardizePath() != Name)
                {
                    type = IwFsType.Invalid;
                    fileSystemInfo = null;
                }
            }

            var ext = System.IO.Path.GetExtension(path);

            var attrs = new List<IwFsAttribute>();

            foreach (var attr in SpecificEnumUtils<FileAttributes>.Values)
            {
                if (fileSystemInfo?.Attributes.HasFlag(attr) == true)
                {
                    switch (attr)
                    {
                        case FileAttributes.Hidden:
                            attrs.Add(IwFsAttribute.Hidden);
                            break;
                        case FileAttributes.ReparsePoint:
                            type = IwFsType.Symlink;
                            break;
                        default:
                            break;
                    }
                }
            }

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

            Ext = ext;
            Type = type;
            MeaningfulName = System.IO.Path.GetFileNameWithoutExtension(Name);
            CreationTime = fileSystemInfo?.CreationTime;
            LastWriteTime = fileSystemInfo?.LastWriteTime;
            Attributes = attrs.ToArray();
            ChildrenCount = fileSystemInfo is FileInfo ? 0 : null;
            // FileCount = fileSystemInfo is FileInfo ? 0 : null;
            // DirectoryCount = fileSystemInfo is FileInfo ? 0 : null;

            if (Type.CanBeDecompressed())
            {
                PasswordsForDecompressing = path.GetPasswordsFromPath();
            }

            try
            {
                Size = fileSystemInfo is FileInfo f ? f.Length : null;
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public string Path { get; set; }
        public string Name { get; set; }
        public string MeaningfulName { get; set; }
        public string Ext { get; set; }
        public IwFsAttribute[] Attributes { get; set; }
        public IwFsType Type { get; set; }
        public long? Size { get; set; }

        public int? ChildrenCount { get; set; }

        // public int? FileCount { get; set; }
        // public int? DirectoryCount { get; set; }
        public DateTime? CreationTime { get; set; }
        public DateTime? LastWriteTime { get; set; }
        public string[] PasswordsForDecompressing { get; set; } = Array.Empty<string>();

        public override string ToString()
        {
            return Path;
        }
    }
}