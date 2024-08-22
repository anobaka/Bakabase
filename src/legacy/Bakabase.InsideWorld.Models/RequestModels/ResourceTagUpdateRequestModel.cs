using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class ResourceTagUpdateRequestModel
    {
        [BindRequired] [Required] public Dictionary<int, int[]> ResourceTagIds { get; set; } = null!;
    }
}