using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    [Obsolete]
    public class CustomPlayerOptionsDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Executable { get; set; }
        public string CommandTemplate { get; set; }

        public List<SubCustomPlayerOptionsDto> SubCustomPlayerOptionsList { get; set; } = new();
    }
}