using Bakabase.InsideWorld.Models.Models.Dtos;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Configs.CustomOptions
{
    public class CustomPlayerOptions
    {
        [Required] public string Executable { get; set; }
        public string CommandTemplate { get; set; }
        public List<ExtensionBasedPlayerOptions> SubPlayers { get; set; } = new();

        public class ExtensionBasedPlayerOptions
        {
            [Required] public string[] Extensions { get; set; }
            [Required] public string Executable { get; set; }
            public string CommandTemplate { get; set; }
        }
    }
}