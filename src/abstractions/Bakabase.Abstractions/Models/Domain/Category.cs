using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Components.Doc.Swagger;

namespace Bakabase.Abstractions.Models.Domain
{
    [SwaggerCustomModel]
    public record Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Color { get; set; }
        public DateTime CreateDt { get; set; }
        public bool IsValid { get; set; }

        /// <summary>
        /// Stores some temporarily message showing on GUI. Such as the reason why the category is invalid
        /// </summary>
        [Obsolete]
        public string? Message { get; set; }

        public int Order { get; set; }
        public CategoryComponent[]? ComponentsData { get; set; }
        public CoverSelectOrder CoverSelectionOrder { get; set; }
        public ResourceCategoryEnhancementOptions? EnhancementOptions { get; set; }
        public bool GenerateNfo { get; set; }

        public List<CustomProperty>? CustomProperties { get; set; }
        public List<CategoryEnhancerOptions>? EnhancerOptions { get; set; }
    }
}