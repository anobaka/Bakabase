using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer.Entries
{
    public class IwFsCompressedFileGroup : CompressedFileGroup
    {
        public string? Password { get; set; }
        public List<string> PasswordCandidates { get; set; } = new();
    }
}