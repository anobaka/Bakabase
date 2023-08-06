using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Compression
{
    public class CompressedFileEntry
    {
        public string Path { get; set; }
        public long Size { get; set; }

        public decimal SizeInMb => Size / 1024m / 1024m;
    }
}
