using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class ResourceSetMediaLibrariesRequestModel
    {
        [Required] [BindRequired] public int[] Ids { get; set; } = null!;
        [Required] [BindRequired] public int[] MediaLibraryIds { get; set; } = null!;
    }
}