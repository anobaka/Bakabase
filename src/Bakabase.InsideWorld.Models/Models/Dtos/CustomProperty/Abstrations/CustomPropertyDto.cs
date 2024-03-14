using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations
{
    public record CustomPropertyDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public CustomPropertyType Type { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<ResourceCategoryDto>? Categories { get; set; }
    }

    public record CustomPropertyDto<T> : CustomPropertyDto
    {
        public T? Options { get; set; }
    }
}
