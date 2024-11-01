using System.Linq;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Service.Models.View;

namespace Bakabase.Service.Extensions;

public static class CategoryExtensions
{
    public static CategoryViewModel ToViewModel(this Category model)
    {
        return new CategoryViewModel
        {
            Id = model.Id,
            Name = model.Name,
            Color = model.Color,
            ComponentsData = model.ComponentsData,
            CoverSelectionOrder = model.CoverSelectionOrder,
            CreateDt = model.CreateDt,
            GenerateNfo = model.GenerateNfo,
            EnhancerOptions = model.EnhancerOptions,
            Order = model.Order,
            ResourceDisplayNameTemplate = model.ResourceDisplayNameTemplate,
            CustomProperties = model.CustomProperties?.Select(c => new CategoryViewModel.CustomPropertyViewModel
                {Id = c.Id, Name = c.Name}).ToList()
        };
    }
}