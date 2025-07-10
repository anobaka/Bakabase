using System.Collections.Concurrent;
using System.Reflection;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bootstrap.Extensions;

namespace Bakabase.Modules.Property.Components;

public record PropertyInternals
{
    public static readonly ConcurrentBag<IPropertySearchHandler> PropertySearchHandlers = new(Assembly
        .GetExecutingAssembly().GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } &&
                                                      t.IsAssignableTo(SpecificTypeUtils<IPropertySearchHandler>
                                                          .Type))
        .Select(x => (Activator.CreateInstance(x) as IPropertySearchHandler)!));

    public static readonly ConcurrentDictionary<PropertyType, IPropertySearchHandler> PropertySearchHandlerMap =
        new ConcurrentDictionary<PropertyType, IPropertySearchHandler>(
            PropertySearchHandlers.ToDictionary(d => d.Type, d => d));

    public static ConcurrentDictionary<PropertyType, PropertyAttribute> PropertyAttributeMap =
        new ConcurrentDictionary<PropertyType, PropertyAttribute>(new Dictionary<PropertyType, PropertyAttribute>
        {
            {
                PropertyType.SingleLineText,
                new PropertyAttribute(StandardValueType.String, StandardValueType.String, false)
            },
            {
                PropertyType.MultilineText,
                new PropertyAttribute(StandardValueType.String, StandardValueType.String, false)
            },
            {
                PropertyType.SingleChoice,
                new PropertyAttribute(StandardValueType.String, StandardValueType.String, true)
            },
            {
                PropertyType.MultipleChoice,
                new PropertyAttribute(StandardValueType.ListString, StandardValueType.ListString, true)
            },
            {PropertyType.Number, new PropertyAttribute(StandardValueType.Decimal, StandardValueType.Decimal, false)},
            {
                PropertyType.Percentage,
                new PropertyAttribute(StandardValueType.Decimal, StandardValueType.Decimal, false)
            },
            {PropertyType.Rating, new PropertyAttribute(StandardValueType.Decimal, StandardValueType.Decimal, false)},
            {PropertyType.Boolean, new PropertyAttribute(StandardValueType.Boolean, StandardValueType.Boolean, false)},
            {PropertyType.Link, new PropertyAttribute(StandardValueType.Link, StandardValueType.Link, false)},
            {
                PropertyType.Attachment,
                new PropertyAttribute(StandardValueType.ListString, StandardValueType.ListString, false)
            },
            {PropertyType.Date, new PropertyAttribute(StandardValueType.DateTime, StandardValueType.DateTime, false)},
            {
                PropertyType.DateTime,
                new PropertyAttribute(StandardValueType.DateTime, StandardValueType.DateTime, false)
            },
            {PropertyType.Time, new PropertyAttribute(StandardValueType.Time, StandardValueType.Time, false)},
            {PropertyType.Formula, new PropertyAttribute(StandardValueType.String, StandardValueType.String, false)},
            {
                PropertyType.Multilevel,
                new PropertyAttribute(StandardValueType.ListString, StandardValueType.ListListString, true)
            },
            {PropertyType.Tags, new PropertyAttribute(StandardValueType.ListString, StandardValueType.ListTag, true)}
        });

    public static ConcurrentDictionary<ResourceProperty, Bakabase.Abstractions.Models.Domain.Property>
        BuiltinPropertyMap { get; } =
        new(
            new[]
            {
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.Filename, PropertyType.SingleLineText),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.DirectoryPath, PropertyType.SingleLineText),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.CreatedAt, PropertyType.DateTime),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.FileCreatedAt, PropertyType.DateTime),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.FileModifiedAt, PropertyType.DateTime),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.Category, PropertyType.SingleChoice),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.MediaLibraryV2, PropertyType.SingleChoice),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.MediaLibrary, PropertyType.Multilevel),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.PlayedAt, PropertyType.DateTime),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.ParentResource, PropertyType.SingleChoice),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Reserved,
                    (int) ResourceProperty.Rating, PropertyType.Rating),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Reserved,
                    (int) ResourceProperty.Introduction, PropertyType.MultilineText),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Reserved,
                    (int) ResourceProperty.Cover, PropertyType.Attachment),
            }.ToDictionary(d => (ResourceProperty) d.Id, d => d));

    public static readonly ConcurrentDictionary<ResourceProperty, Bakabase.Abstractions.Models.Domain.Property>
        InternalPropertyMap =
            new(new[]
            {
                ResourceProperty.Filename,
                ResourceProperty.DirectoryPath,
                ResourceProperty.CreatedAt,
                ResourceProperty.FileCreatedAt,
                ResourceProperty.FileModifiedAt,
                ResourceProperty.Category,
                ResourceProperty.MediaLibrary,
                ResourceProperty.PlayedAt,
                ResourceProperty.MediaLibraryV2,
                ResourceProperty.ParentResource,
            }.ToDictionary(d => d, d => BuiltinPropertyMap[d]));

    public static readonly ConcurrentDictionary<ReservedProperty, Bakabase.Abstractions.Models.Domain.Property>
        ReservedPropertyMap =
            new(new[]
            {
                ResourceProperty.Rating,
                ResourceProperty.Introduction,
                ResourceProperty.Cover,
            }.ToDictionary(
                d => (ReservedProperty) d, d => BuiltinPropertyMap[d]));

    public static readonly
        ConcurrentDictionary<SearchableReservedProperty, Bakabase.Abstractions.Models.Domain.Property>
        SearchableResourcePropertyDescriptorMap =
            new ConcurrentDictionary<SearchableReservedProperty, Bakabase.Abstractions.Models.Domain.Property>(
                SpecificEnumUtils<SearchableReservedProperty>.Values.Select(x =>
                        InternalPropertyMap.GetValueOrDefault((ResourceProperty) x) ??
                        ReservedPropertyMap.GetValueOrDefault((ReservedProperty) x))
                    .OfType<Bakabase.Abstractions.Models.Domain.Property>()
                    .ToDictionary(d => (SearchableReservedProperty) d.Id, d => d));

    public static
        Dictionary<PropertyType,
            Dictionary<PropertyType, List<(object? FromBizValue, object? ExpectedBizValue)>>>
        ExpectedConversions = StandardValueInternals.ExpectedConversions.SelectMany(
            x =>
            {
                var fromTypes = x.Key.GetCompatibleCustomPropertyTypes() ?? [];
                return fromTypes.Select(fromType =>
                {
                    var toTypeValueMap = x.Value;
                    return (fromType, toTypeValueMap.SelectMany(y =>
                    {
                        var toTypes = y.Key.GetCompatibleCustomPropertyTypes() ?? [];
                        return toTypes.Select(toType => (toType, y.Value));
                    }).ToDictionary(d => d.toType, d => d.Value));
                });
            }).ToDictionary(d => d.fromType, d => d.Item2);

    public static readonly ConcurrentBag<IPropertyDescriptor> Descriptors =
        new ConcurrentBag<IPropertyDescriptor>(Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t is {IsClass: true, IsAbstract: false, IsPublic: true} &&
                        t.IsAssignableTo(SpecificTypeUtils<IPropertyDescriptor>.Type))
            .Select(x => (Activator.CreateInstance(x) as IPropertyDescriptor)!));

    public static readonly ConcurrentDictionary<PropertyType, IPropertyDescriptor> DescriptorMap =
        new ConcurrentDictionary<PropertyType, IPropertyDescriptor>(Descriptors.ToDictionary(d => d.Type, d => d));

    public static readonly ConcurrentDictionary<PropertyType, Bakabase.Abstractions.Models.Domain.Property>
        VirtualPropertyMap = new ConcurrentDictionary<PropertyType, Bakabase.Abstractions.Models.Domain.Property>(
            Descriptors.Select(d =>
                    new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Custom, 0, d.Type,
                        "Virtual property"))
                .ToDictionary(d => d.Type, d => d));
}