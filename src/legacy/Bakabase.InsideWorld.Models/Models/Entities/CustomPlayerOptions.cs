using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    [Obsolete]
    public class CustomPlayerOptions
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string CommandTemplate { get; set; }
        public string Executable { get; set; }
        public string SubCustomPlayerOptionsListJson { get; set; }
    }
}