using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer.Entries
{
    public class IwFsCompressedFileGroup : CompressedFileHelper.CompressedFileGroup
    {
        public string? Password { get; set; }
        public List<string> PasswordCandidates { get; set; } = new();
    }
}