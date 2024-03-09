using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.InsideWorld.Models.Models.Entities;

public record CategoryComponent
{
    public int Id { get; set; }
    [BindRequired] [Required] public int CategoryId { get; set; }

    [Required] public string ComponentKey { get; set; } = null!;

    // public string ComponentAssemblyQualifiedTypeName { get; set; }
    // public int? OptionsId { get; set; }
    [Required] [BindRequired] public ComponentType ComponentType { get; set; }

    /// <summary>
    /// todo: move to dto
    /// </summary>
    [NotMapped] public ComponentDescriptor Descriptor { get; set; } = null!;

    public CategoryComponent Duplicate(int toCategoryId)
    {
        return this with
        {
            Id = 0,
            CategoryId = toCategoryId
        };
    }
}