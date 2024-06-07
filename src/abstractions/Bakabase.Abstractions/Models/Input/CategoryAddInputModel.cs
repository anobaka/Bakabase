using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Org.BouncyCastle.Asn1.Cms;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class CategoryAddInputModel : CategoryPatchInputModel
    {
        public SimpleCategoryComponent[] ComponentsData { get; set; } = Array.Empty<SimpleCategoryComponent>();
        public ResourceCategoryEnhancementOptions? EnhancementOptions { get; set; }

        public class SimpleCategoryComponent
        {
            [Required] public string ComponentKey { get; set; } = string.Empty;

            // public string ComponentAssemblyQualifiedTypeName { get; set; }
            // public int? OptionsId { get; set; }
            [Required][BindRequired] public ComponentType ComponentType { get; set; }
        }
    }
}