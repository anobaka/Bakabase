using System.Collections.Concurrent;
using System.Reflection;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue;
using Bootstrap.Extensions;

namespace Bakabase.Modules.Property.Components;

/// <summary>
/// Internal implementation details for the Property system.
/// For public API usage, prefer PropertySystem, PropertyValueFactory, and BuiltinProperties.
/// </summary>
internal static class PropertyInternals
{
    private static readonly ConcurrentBag<IPropertySearchHandler> PropertySearchHandlers = new(Assembly
        .GetExecutingAssembly().GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } &&
                                                      t.IsAssignableTo(SpecificTypeUtils<IPropertySearchHandler>
                                                          .Type))
        .Select(x => (Activator.CreateInstance(x) as IPropertySearchHandler)!));

    /// <summary>
    /// Search handlers by property type. Use PropertySystem.Property.GetSearchHandler() for public access.
    /// </summary>
    [Obsolete("Use PropertySystem.Property.GetSearchHandler() or TryGetSearchHandler() instead.")]
    public static readonly ConcurrentDictionary<PropertyType, IPropertySearchHandler> PropertySearchHandlerMap =
        new ConcurrentDictionary<PropertyType, IPropertySearchHandler>(
            PropertySearchHandlers.ToDictionary(d => d.Type, d => d));

    /// <summary>
    /// Property type attributes. Use PropertySystem.Property.GetAttribute() for public access.
    /// </summary>
    [Obsolete("Use PropertySystem.Property.GetAttribute() instead.")]
    public static readonly ConcurrentDictionary<PropertyType, PropertyAttribute> PropertyAttributeMap =
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

    /// <summary>
    /// Built-in property definitions. Use PropertySystem.Builtin.Get() or BuiltinProperties.Get() for public access.
    /// </summary>
    [Obsolete("Use PropertySystem.Builtin.Get() or BuiltinProperties.Get() instead.")]
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
                    (int) ResourceProperty.MediaLibraryV2Multi, PropertyType.MultipleChoice),
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

    /// <summary>
    /// Internal property definitions. Use BuiltinProperties.Internal.* for public access.
    /// </summary>
    [Obsolete("Use BuiltinProperties.Internal.* instead.")]
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
                ResourceProperty.MediaLibraryV2Multi,
                ResourceProperty.ParentResource,
            }.ToDictionary(d => d, d => BuiltinPropertyMap[d]));

    /// <summary>
    /// Reserved property definitions. Use BuiltinProperties.Reserved.* for public access.
    /// </summary>
    [Obsolete("Use BuiltinProperties.Reserved.* instead.")]
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

    /// <summary>
    /// Expected conversion test data for property types. Use PropertySystem.Property.GetExpectedConversions() for public access.
    /// </summary>
    public static readonly Dictionary<PropertyType,
            Dictionary<PropertyType, List<(object? FromBizValue, object? ExpectedBizValue)>>>
        ExpectedConversions = StandardValueSystem.GetExpectedConversions().SelectMany(
            x =>
            {
                var fromTypes = x.Key.GetCompatibleCustomPropertyTypes() ?? [];
                return fromTypes.Select(fromType =>
                {
                    var toTypeValueMap = x.Value;
                    return (fromType, toTypeValueMap.SelectMany(y =>
                    {
                        var toTypes = y.Key.GetCompatibleCustomPropertyTypes() ?? [];
                        return toTypes.Select(toType => (toType, y.Value.Select(v => (v.FromValue, v.ExpectedValue)).ToList()));
                    }).ToDictionary(d => d.toType, d => d.Item2));
                });
            }).ToDictionary(d => d.fromType, d => d.Item2);

    public static readonly ConcurrentBag<IPropertyDescriptor> Descriptors =
        new ConcurrentBag<IPropertyDescriptor>(Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t is {IsClass: true, IsAbstract: false, IsPublic: true} &&
                        t.IsAssignableTo(SpecificTypeUtils<IPropertyDescriptor>.Type))
            .Select(x => (Activator.CreateInstance(x) as IPropertyDescriptor)!));

    /// <summary>
    /// Property descriptors by type. Use PropertySystem.Property.GetDescriptor() for public access.
    /// </summary>
    public static readonly ConcurrentDictionary<PropertyType, IPropertyDescriptor> DescriptorMap =
        new(Descriptors.ToDictionary(d => d.Type, d => d));

    /// <summary>
    /// Virtual property instances by type. Use PropertySystem.Property.GetVirtual() for public access.
    /// </summary>
    [Obsolete("Use PropertySystem.Property.GetVirtual() or TryGetVirtual() instead.")]
    public static readonly ConcurrentDictionary<PropertyType, Bakabase.Abstractions.Models.Domain.Property>
        VirtualPropertyMap = new ConcurrentDictionary<PropertyType, Bakabase.Abstractions.Models.Domain.Property>(
            Descriptors.Select(d =>
                    new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Custom, 0, d.Type,
                        "Virtual property"))
                .ToDictionary(d => d.Type, d => d));
}