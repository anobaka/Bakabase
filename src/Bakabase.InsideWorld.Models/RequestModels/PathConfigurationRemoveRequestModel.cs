using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class PathConfigurationRemoveRequestModel
    {
        [Required] public string Path { get; set; } = string.Empty;
    }
}