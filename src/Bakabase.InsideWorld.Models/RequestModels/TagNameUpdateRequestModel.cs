using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class TagNameUpdateRequestModel
    {
        [Required] public string Name { get; set; } = null!;
    }
}
