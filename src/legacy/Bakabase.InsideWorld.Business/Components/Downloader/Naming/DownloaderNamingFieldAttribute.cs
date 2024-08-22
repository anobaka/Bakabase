using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Naming
{
    [AttributeUsage(AttributeTargets.Field)]
    public class DownloaderNamingFieldAttribute : Attribute
    {
        public string Description { get; set; }
        public string Example { get; set; }
    }
}
