using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.InsideWorld.Business.Models.Dto
{
    public class MediaLibraryCreateDto
    {
        [Required] public string Name { get; set; } = string.Empty;
        [Required][BindRequired] public int CategoryId { get; set; }
        public List<PathConfiguration>? PathConfigurations { get; set; }
    }
}