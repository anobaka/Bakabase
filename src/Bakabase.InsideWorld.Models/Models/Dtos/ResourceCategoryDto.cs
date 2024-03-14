using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public class ResourceCategoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Color { get; set; }
        public DateTime CreateDt { get; set; }
        public bool IsValid { get; set; }
        /// <summary>
        /// Stores some temporarily message showing on GUI. Such as the reason why the category is invalid
        /// </summary>
        public string Message { get; set; }
        public int Order { get; set; }
        public CategoryComponent[] ComponentsData { get; set; }
        public CoverSelectOrder CoverSelectionOrder { get; set; }
        public ResourceCategoryEnhancementOptions EnhancementOptions { get; set; }
        public bool GenerateNfo { get; set; }

        public List<CustomPropertyDto>? CustomProperties { get; set; }
    }
}