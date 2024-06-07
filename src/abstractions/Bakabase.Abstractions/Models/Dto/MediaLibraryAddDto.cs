using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.Abstractions.Models.Dto
{
    public class MediaLibraryAddDto
    {
        [Required] public string Name { get; set; } = string.Empty;
        [Required][BindRequired] public int CategoryId { get; set; }
        public List<PathConfiguration>? PathConfigurations { get; set; }
    }
}