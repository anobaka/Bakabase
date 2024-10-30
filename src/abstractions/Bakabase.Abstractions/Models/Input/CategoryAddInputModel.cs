using System.ComponentModel.DataAnnotations;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.Abstractions.Models.Input
{
    public record CategoryAddInputModel 
        // : CategoryPatchInputModel
    {
        public string Name { get; set; } = null!;
        // public SimpleCategoryComponent[] ComponentsData { get; set; } = Array.Empty<SimpleCategoryComponent>();
        // public ResourceCategoryEnhancementOptions? EnhancementOptions { get; set; }
        //
        // public class SimpleCategoryComponent
        // {
        //     [Required] public string ComponentKey { get; set; } = string.Empty;
        //
        //     // public string ComponentAssemblyQualifiedTypeName { get; set; }
        //     // public int? OptionsId { get; set; }
        //     [Required][BindRequired] public ComponentType ComponentType { get; set; }
        // }
    }
}