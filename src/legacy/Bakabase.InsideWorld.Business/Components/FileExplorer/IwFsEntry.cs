using System;
using System.IO;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business.Extensions;

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
                if (Directory.Exists(path))
                {
                    type = IwFsType.Directory;
                }
                else
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
                            else
                            {
                                if (InternalOptions.CompressedFileExtensions.Contains(ext))
                                {
                                    type = IwFsType.CompressedFileEntry;
                                }
                            }
                        }
                    }
                }
            }

            Ext = type == IwFsType.Unknown ? System.IO.Path.GetExtension(Name) : null;
            Type = type;
            Size = ReadFileSize(path, type);
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
        /// <summary>Current regular-file length; directories and unavailable files have no size.</summary>
        public long? Size { get; set; }

        internal static long? ReadFileSize(string path, IwFsType type)
        {
            if (type is IwFsType.Directory or IwFsType.Drive or IwFsType.Symlink or IwFsType.Invalid)
                return null;
            try
            {
                var file = new FileInfo(path);
                return file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0
                    ? file.Length : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

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
