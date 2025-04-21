using System.Runtime.CompilerServices;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Components.Properties.Time;
using Bakabase.Modules.Property.Extensions;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Property.Services;

public class PropertyService(IServiceProvider serviceProvider, IPropertyLocalizer propertyLocalizer) : IPropertyService
{
    private static SingleChoicePropertyOptions BuildOptionsForCategory(IEnumerable<Category> categories)
    {
        return new SingleChoicePropertyOptions
        {
            Choices = categories.Select(c => new ChoiceOptions()
            {
                Value = c.Id.ToString(),
                Label = c.Name
            }).ToList()
        };
    }

    private static MultilevelPropertyOptions BuildOptionsForMediaLibrary(Dictionary<int, Category> categoryMap,
        List<MediaLibrary> mediaLibraries)
    {
        var categoryIdMediaLibraries =
            mediaLibraries.GroupBy(d => d.CategoryId).ToDictionary(d => d.Key, d => d.ToList());
        return new MultilevelPropertyOptions
        {
            Data = categoryIdMediaLibraries!.Select(kv =>
            {
                var (cId, libraries) = kv;
                var category = categoryMap!.GetValueOrDefault(cId);
                if (category == null)
                {
                    return null;
                }

                return new MultilevelDataOptions()
                {
                    Value = $"c-{cId}",
                    Label = category.Name,
                    Children = libraries.Select(l => new MultilevelDataOptions
                    {
                        Label = l.Name,
                        Value = l.Id.ToString()
                    }).ToList()
                };
            }).OfType<MultilevelDataOptions>().ToList(),
            ValueIsSingleton = true
        };
    }

    public async Task<Bakabase.Abstractions.Models.Domain.Property> GetProperty(PropertyPool pool, int id)
    {
        switch (pool)
        {
            case PropertyPool.Internal:
            {
                var rp = (ResourceProperty) id;
                var tmpProperty = PropertyInternals.BuiltinPropertyMap[rp] with
                {
                    Name = propertyLocalizer.BuiltinPropertyName(rp)
                };

                Dictionary<int, Category>? categoryMap = null;
                List<MediaLibrary>? mediaLibraries = null;

                if (rp == ResourceProperty.Category || rp == ResourceProperty.MediaLibrary)
                {
                    var categoryService = serviceProvider.GetRequiredService<ICategoryService>();
                    var categories = await categoryService.GetAll();
                    categoryMap = categories.ToDictionary(d => d.Id, d => d);
                }

                if (rp == ResourceProperty.MediaLibrary)
                {
                    var mediaLibraryService = serviceProvider.GetRequiredService<IMediaLibraryService>();
                    mediaLibraries =
                        await mediaLibraryService.GetAll(null, MediaLibraryAdditionalItem.Category);
                }

                switch ((InternalProperty) id)
                {
                    case InternalProperty.Category:
                    {
                        tmpProperty.Options = BuildOptionsForCategory(categoryMap!.Values);
                        break;
                    }
                    case InternalProperty.MediaLibrary:
                    {
                        tmpProperty.Options = BuildOptionsForMediaLibrary(categoryMap!, mediaLibraries!);
                        break;
                    }
                    case InternalProperty.RootPath:
                    case InternalProperty.ParentResource:
                    case InternalProperty.Resource:
                    case InternalProperty.Filename:
                    case InternalProperty.DirectoryPath:
                    case InternalProperty.CreatedAt:
                    case InternalProperty.FileCreatedAt:
                    case InternalProperty.FileModifiedAt:
                    default:
                        break;
                }

                return tmpProperty;
            }
            case PropertyPool.Reserved:
                return PropertyInternals.BuiltinPropertyMap[(ResourceProperty) id] with
                {
                    Name = propertyLocalizer.BuiltinPropertyName((ResourceProperty) id)
                };
            case PropertyPool.Custom:
                return (await serviceProvider.GetRequiredService<ICustomPropertyService>().GetByKey(id)).ToProperty();
            case PropertyPool.All:
            default:
                throw new ArgumentOutOfRangeException(nameof(pool), pool, null);
        }
    }

    public async Task<List<Bakabase.Abstractions.Models.Domain.Property>> GetProperties(PropertyPool pool)
    {
        var properties = new List<Bakabase.Abstractions.Models.Domain.Property>();

        foreach (var p in SpecificEnumUtils<PropertyPool>.Values)
        {
            if (pool.HasFlag(p))
            {
                switch (p)
                {
                    case PropertyPool.Internal:
                    {
                        var categoryService = serviceProvider.GetRequiredService<ICategoryService>();
                        var categories = await categoryService.GetAll();
                        var categoryMap = categories.ToDictionary(d => d.Id, d => d);
                        var mediaLibraryService = serviceProvider.GetRequiredService<IMediaLibraryService>();
                        var mediaLibraries =
                            await mediaLibraryService.GetAll(null, MediaLibraryAdditionalItem.Category);
                        var internalProperties = PropertyInternals.InternalPropertyMap.Values.Select(v =>
                        {
                            var tmpProperty = v with
                            {
                                Name = propertyLocalizer.BuiltinPropertyName((ResourceProperty) v.Id)
                            };
                            switch ((InternalProperty) v.Id)
                            {
                                case InternalProperty.Category:
                                {
                                    tmpProperty.Options = BuildOptionsForCategory(categoryMap!.Values);
                                    break;
                                }
                                case InternalProperty.MediaLibrary:
                                {
                                    tmpProperty.Options = BuildOptionsForMediaLibrary(categoryMap!, mediaLibraries!);
                                    break;
                                }
                                case InternalProperty.RootPath:
                                case InternalProperty.ParentResource:
                                case InternalProperty.Resource:
                                case InternalProperty.Filename:
                                case InternalProperty.DirectoryPath:
                                case InternalProperty.CreatedAt:
                                case InternalProperty.FileCreatedAt:
                                case InternalProperty.FileModifiedAt:
                                default:
                                    break;
                            }

                            return tmpProperty;
                        });
                        properties.AddRange(internalProperties);
                        break;
                    }
                    case PropertyPool.Reserved:
                    {
                        var reservedProperties = PropertyInternals.ReservedPropertyMap.Values.Select(v =>
                            v with {Name = propertyLocalizer.BuiltinPropertyName((ResourceProperty) v.Id)});
                        properties.AddRange(reservedProperties);
                        break;
                    }
                    case PropertyPool.Custom:
                    {
                        var customPropertyService = serviceProvider.GetRequiredService<ICustomPropertyService>();
                        var customProperties = await customPropertyService.GetAll();
                        properties.AddRange(customProperties.Select(c => c.ToProperty()));
                        break;
                    }
                    case PropertyPool.All:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        return properties;
    }
}