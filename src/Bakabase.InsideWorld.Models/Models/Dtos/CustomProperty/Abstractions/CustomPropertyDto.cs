using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions
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
