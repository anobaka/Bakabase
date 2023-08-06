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

    [Required] public string ComponentKey { get; set; }

    // public string ComponentAssemblyQualifiedTypeName { get; set; }
    // public int? OptionsId { get; set; }
    [Required] [BindRequired] public ComponentType ComponentType { get; set; }
    [NotMapped] public ComponentDescriptor Descriptor { get; set; }
}