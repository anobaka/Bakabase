using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    [Obsolete]
    public class CustomPlayableFileSelectorOptionsDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int MaxFileCount { get; set; }
        public string[] Extensions { get; set; }
        public string ExtensionsString { get; set; }
        public bool Valid => true;
    }
}