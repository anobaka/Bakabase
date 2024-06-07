using Bakabase.InsideWorld.Models.Models.Dtos;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class CategoryComponentConfigureInputModel
    {
        [BindRequired, Required]
        public ComponentType Type { get; set; }

        [BindRequired, Required] public string[] ComponentKeys { get; set; } = Array.Empty<string>();
        public ResourceCategoryEnhancementOptions? EnhancementOptions { get; set; }
    }
}
