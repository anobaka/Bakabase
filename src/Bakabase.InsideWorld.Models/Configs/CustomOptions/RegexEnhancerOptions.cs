using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Configs.CustomOptions
{
    public class RegexEnhancerOptions
    {
        [Required] public string Regex { get; set; }
        [Required] public Group[] Groups { get; set; }

        public class Group
        {
            [Required] public string Name { get; set; }
            [Required] public bool IsReserved { get; set; }
            [Required] public string Key { get; set; }
        }
    }
}