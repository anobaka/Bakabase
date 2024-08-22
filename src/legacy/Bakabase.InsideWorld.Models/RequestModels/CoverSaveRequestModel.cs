using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class CoverSaveRequestModel
    {
        [Required] public string Base64Image { get; set; } = null!;
        public bool Overwrite { get; set; }
        public CoverSaveLocation? SaveLocation { get; set; }
    }
}