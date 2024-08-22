using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    [Obsolete]
    public class CustomPlayableFileSelectorOptions
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int MaxFileCount { get; set; }
        public string ExtensionsString { get; set; }
        public const char ExtensionSeparator = ',';
    }
}