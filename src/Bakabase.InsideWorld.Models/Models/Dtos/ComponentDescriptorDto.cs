using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public record ComponentDescriptorDto : ComponentDescriptor
    {
        public ResourceCategoryDto[] AssociatedCategories { get; set; }
    }
}