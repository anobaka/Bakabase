using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer.Information
{
    public class IwFsEntryLazyInfo
    {
        public IwFsEntryLazyInfo(string path, IwFsType type)
        {
            if (type is IwFsType.Directory or IwFsType.Drive)
            {
                ChildrenCount = Directory.GetFileSystemEntries(path).Length;
            }

            var name = Path.GetFileName(path);

            if (string.IsNullOrEmpty(name))
            {
                name = path;
            }

            FileSystemInfo? fileSystemInfo = null;
            if (type == IwFsType.Unknown)
            {
                if (Directory.Exists(path))
                {
                    var di = new DirectoryInfo(path);
                    type = di.Parent == null ? IwFsType.Drive : IwFsType.Directory;
                    fileSystemInfo = di;
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
                    if (fileSystemInfo.Name.StandardizePath() != name)
                    {
                        type = IwFsType.Invalid;
                        fileSystemInfo = null;
                    }
                }
            }

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
                        case FileAttributes.None:
                        case FileAttributes.ReadOnly:
                        case FileAttributes.System:
                        case FileAttributes.Directory:
                        case FileAttributes.Archive:
                        case FileAttributes.Device:
                        case FileAttributes.Normal:
                        case FileAttributes.Temporary:
                        case FileAttributes.SparseFile:
                        case FileAttributes.Compressed:
                        case FileAttributes.Offline:
                        case FileAttributes.NotContentIndexed:
                        case FileAttributes.Encrypted:
                        case FileAttributes.IntegrityStream:
                        case FileAttributes.NoScrubData:
                        default:
                            break;
                    }
                }
            }

            try
            {
                Size = fileSystemInfo is FileInfo f ? f.Length : null;
            }
            catch (Exception)
            {
                // ignored
            }

            Type = type;
            Attributes = attrs.ToArray();
        }

        public int ChildrenCount { get; set; }
        public IwFsAttribute[] Attributes { get; } = [];
        public IwFsType Type { get; }
        public long? Size { get; set; }
    }
}