using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Bakabase.InsideWorld.Models.Models.Entities.Implicit;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class MediaLibraryCreateRequestModel
    {
        [Required]public string Name { get; set; } = string.Empty;
        [Required] [BindRequired] public int CategoryId { get; set; }

        public List<PathConfigurationDto>? PathConfigurations { get; set; }

    }
}