using System.ComponentModel.DataAnnotations;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.Abstractions.Models.Input
{
    public class CategoryComponentConfigureInputModel
    {
        [BindRequired, Required]
        public ComponentType Type { get; set; }

        [BindRequired, Required] public string[] ComponentKeys { get; set; } = Array.Empty<string>();
        public ResourceCategoryEnhancementOptions? EnhancementOptions { get; set; }
    }
}
