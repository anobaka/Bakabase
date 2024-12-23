using Bakabase.InsideWorld.Models.Constants;
using System.Collections.Generic;
using System;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using CategoryEnhancerOptions = Bakabase.Abstractions.Models.Domain.CategoryEnhancerOptions;

namespace Bakabase.Service.Models.View;

public record CategoryViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Color { get; set; }
    public DateTime CreateDt { get; set; }

    public int Order { get; set; }
    public CategoryComponent[]? ComponentsData { get; set; }
    public CoverSelectOrder CoverSelectionOrder { get; set; }
    public bool GenerateNfo { get; set; }
    public string? ResourceDisplayNameTemplate { get; set; }

    public List<CustomPropertyViewModel>? CustomProperties { get; set; }
    public List<CategoryEnhancerOptions>? EnhancerOptions { get; set; }

    public record CustomPropertyViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public PropertyType Type { get; set; }
    }
}