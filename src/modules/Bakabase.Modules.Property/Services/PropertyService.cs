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
    private static SingleChoicePropertyOptions BuildOptionsForMediaLibraryV2(List<MediaLibraryV2> mediaLibraries)
    {
        return new SingleChoicePropertyOptions
        {
            Choices = mediaLibraries.Select(m => new ChoiceOptions
            {
                Color = null,
                Label = m.Name,
                Value = m.Id.ToString()
            }).ToList(),
        };
    }

    private static MultipleChoicePropertyOptions BuildOptionsForMediaLibraryV2Multi(List<MediaLibraryV2> mediaLibraries)
    {
        return new MultipleChoicePropertyOptions
        {
            Choices = mediaLibraries.Select(m => new ChoiceOptions
            {
                Color = null,
                Label = m.Name,
                Value = m.Id.ToString()
            }).ToList(),
        };
    }

    private MultipleChoicePropertyOptions BuildOptionsForSource()
    {
        return new MultipleChoicePropertyOptions
        {
            Choices = Enum.GetValues<ResourceSource>().Select(s => new ChoiceOptions
            {
                Color = null,
                Label = propertyLocalizer.ResourceSourceName(s),
                Value = ((int)s).ToString()
            }).ToList(),
        };
    }

    public async Task<Bakabase.Abstractions.Models.Domain.Property> GetProperty(PropertyPool pool, int id)
    {
        switch (pool)
        {
            case PropertyPool.Internal:
            {
                var rp = (ResourceProperty)id;
                var tmpProperty = PropertyInternals.BuiltinPropertyMap[rp] with
                {
                    Name = propertyLocalizer.BuiltinPropertyName(rp)
                };

                List<MediaLibraryV2>? mediaLibrariesV2 = null;

                if (rp == ResourceProperty.MediaLibraryV2 || rp == ResourceProperty.MediaLibraryV2Multi)
                {
                    var mediaLibraryService = serviceProvider.GetRequiredService<IMediaLibraryV2Service>();
                    mediaLibrariesV2 = await mediaLibraryService.GetAll();
                }

                switch ((InternalProperty)id)
                {
                    case InternalProperty.MediaLibraryV2:
                    {
                        tmpProperty.Options = BuildOptionsForMediaLibraryV2(mediaLibrariesV2!);
                        break;
                    }
                    case InternalProperty.MediaLibraryV2Multi:
                    {
                        tmpProperty.Options = BuildOptionsForMediaLibraryV2Multi(mediaLibrariesV2!);
                        break;
                    }
                    case InternalProperty.Source:
                    {
                        tmpProperty.Options = BuildOptionsForSource();
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
                return PropertyInternals.BuiltinPropertyMap[(ResourceProperty)id] with
                {
                    Name = propertyLocalizer.BuiltinPropertyName((ResourceProperty)id)
                };
            case PropertyPool.Custom:
                return (await serviceProvider.GetRequiredService<ICustomPropertyService>().GetByKey(id)).ToProperty();
            case PropertyPool.All:
            default:
                throw new ArgumentOutOfRangeException(nameof(pool), pool, null);
        }
    }

    public async Task<List<Bakabase.Abstractions.Models.Domain.Property>> GetProperties(PropertyPool pool, bool includeDeprecated = true)
    {
        var properties = new List<Bakabase.Abstractions.Models.Domain.Property>();

        // Deprecated internal properties
        var deprecatedProperties = new HashSet<InternalProperty>
        {
            InternalProperty.MediaLibraryV2
        };

        foreach (var p in SpecificEnumUtils<PropertyPool>.Values)
        {
            if (pool.HasFlag(p))
            {
                switch (p)
                {
                    case PropertyPool.Internal:
                    {
                        // Skip loading dependencies if not including deprecated properties
                        List<MediaLibraryV2>? mediaLibrariesV2 = null;

                        if (includeDeprecated)
                        {
                            var mediaLibraryServiceV2 = serviceProvider.GetRequiredService<IMediaLibraryV2Service>();
                            mediaLibrariesV2 = await mediaLibraryServiceV2.GetAll();
                        }
                        var internalProperties = PropertyInternals.InternalPropertyMap.Values
                            .Where(v => includeDeprecated || !deprecatedProperties.Contains((InternalProperty)v.Id))
                            .Select(v =>
                            {
                                var tmpProperty = v with
                                {
                                    Name = propertyLocalizer.BuiltinPropertyName((ResourceProperty)v.Id)
                                };
                                switch ((InternalProperty)v.Id)
                                {
                                    case InternalProperty.MediaLibraryV2:
                                    {
                                        if (mediaLibrariesV2 != null)
                                        {
                                            tmpProperty.Options = BuildOptionsForMediaLibraryV2(mediaLibrariesV2);
                                        }
                                        break;
                                    }
                                    case InternalProperty.MediaLibraryV2Multi:
                                    {
                                        if (mediaLibrariesV2 != null)
                                        {
                                            tmpProperty.Options = BuildOptionsForMediaLibraryV2Multi(mediaLibrariesV2);
                                        }
                                        break;
                                    }
                                    case InternalProperty.Source:
                                    {
                                        tmpProperty.Options = BuildOptionsForSource();
                                        break;
                                    }
                                    case InternalProperty.ParentResource:
                                    case InternalProperty.RootPath:
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
                            v with { Name = propertyLocalizer.BuiltinPropertyName((ResourceProperty)v.Id) });
                        properties.AddRange(reservedProperties);
                        break;
                    }
                    case PropertyPool.Custom:
                    {
                        var customPropertyService = serviceProvider.GetRequiredService<ICustomPropertyService>();
                        var customProperties = await customPropertyService.GetAll();
                        properties.AddRange(customProperties.OrderBy(c => c.Order).Select(c => c.ToProperty()));
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