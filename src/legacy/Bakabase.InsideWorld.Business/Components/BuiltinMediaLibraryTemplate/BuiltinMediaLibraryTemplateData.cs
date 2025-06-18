using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate;

public record BuiltinMediaLibraryTemplateData
{
    public static Dictionary<BuiltinMediaLibraryTemplateProperty, Property> PropertyMap = new()
    {
        {
            BuiltinMediaLibraryTemplateProperty.Name,
            new Property(
                Pool: PropertyPool.Custom,
                Id: 0,
                Type: PropertyType.SingleLineText
            )
        },
        {
            BuiltinMediaLibraryTemplateProperty.ReleaseDate,
            new Property(
                Pool: PropertyPool.Custom,
                Id: 0,
                Type: PropertyType.Date
            )
        },
        {
            BuiltinMediaLibraryTemplateProperty.Author,
            new Property(
                Pool: PropertyPool.Custom,
                Id: 0,
                Type: PropertyType.SingleChoice
            )
        },
        {
            BuiltinMediaLibraryTemplateProperty.Publisher,
            new Property(
                Pool: PropertyPool.Custom,
                Id: 0,
                Type: PropertyType.SingleChoice
            )
        },
        {
            BuiltinMediaLibraryTemplateProperty.Year,
            new Property(
                Pool: PropertyPool.Custom,
                Id: 0,
                Type: PropertyType.Number
            )
        },
        {
            BuiltinMediaLibraryTemplateProperty.Series,
            new Property(
                Pool: PropertyPool.Custom,
                Id: 0,
                Type: PropertyType.SingleChoice
            )
        },
        {
            BuiltinMediaLibraryTemplateProperty.Tag,
            new Property(
                Pool: PropertyPool.Custom,
                Id: 0,
                Type: PropertyType.Tags
            )
        }
    };
}