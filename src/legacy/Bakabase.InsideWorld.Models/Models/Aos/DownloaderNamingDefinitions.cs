using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public class DownloaderNamingDefinitions
    {
        public Field[] Fields { get; set; }
        public string DefaultConvention { get; set; }

        public class Field
        {
            public string Key { get; set; }
            public string Description { get; set; }
            public string Example { get; set; }
        }
    }
}