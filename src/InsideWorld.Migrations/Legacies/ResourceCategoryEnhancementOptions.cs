using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace InsideWorld.Migrations.Legacies
{
    [Obsolete]
    internal record class ResourceCategoryEnhancementOptions : Bakabase.InsideWorld.Models.Models.Dtos.ResourceCategoryEnhancementOptions
    {
        public string[]? EnhancerAssemblyQualifiedTypeNames { get; set; }
    }
}
