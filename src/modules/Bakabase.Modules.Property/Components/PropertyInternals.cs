using System.Collections.Concurrent;
using System.Reflection;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
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
    public static readonly ConcurrentDictionary<PropertyType, IPropertySearchHandler> PropertySearchHandlerMap =
        new ConcurrentDictionary<PropertyType, IPropertySearchHandler>(
            PropertySearchHandlers.ToDictionary(d => d.Type, d => d));

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
    /// Property type attributes, generated from the descriptors — the descriptor is the single
    /// source of truth for db/biz value types and reference-ness (a new PropertyType without a
    /// descriptor fails here at type-load instead of drifting silently).
    /// Use PropertySystem.Property.GetAttribute() for public access.
    /// </summary>
    public static readonly ConcurrentDictionary<PropertyType, PropertyAttribute> PropertyAttributeMap =
        new(SpecificEnumUtils<PropertyType>.Values.ToDictionary(t => t, t =>
        {
            var d = DescriptorMap.GetValueOrDefault(t) ??
                    throw new InvalidOperationException(
                        $"No {nameof(IPropertyDescriptor)} found for {nameof(PropertyType)}.{t}");
            return new PropertyAttribute(d.DbValueType, d.BizValueType, d.IsReferenceValueType);
        }));

    /// <summary>
    /// Built-in property definitions. Use PropertySystem.Builtin.Get() or BuiltinProperties.Get() for public access.
    /// </summary>
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
                    (int) ResourceProperty.MediaLibraryV2, PropertyType.SingleChoice),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.MediaLibraryV2Multi, PropertyType.MultipleChoice),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.PlayedAt, PropertyType.DateTime),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.ParentResource, PropertyType.SingleChoice),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.Source, PropertyType.MultipleChoice),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Internal,
                    (int) ResourceProperty.HealthScore, PropertyType.Number),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Reserved,
                    (int) ResourceProperty.Rating, PropertyType.Rating),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Reserved,
                    (int) ResourceProperty.Introduction, PropertyType.MultilineText),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Reserved,
                    (int) ResourceProperty.Cover, PropertyType.Attachment),
                new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Reserved,
                    (int) ResourceProperty.Name, PropertyType.SingleLineText),
            }.ToDictionary(d => (ResourceProperty) d.Id, d => d));

    /// <summary>
    /// Internal property definitions (a filtered view over BuiltinPropertyMap).
    /// </summary>
    public static readonly ConcurrentDictionary<ResourceProperty, Bakabase.Abstractions.Models.Domain.Property>
        InternalPropertyMap =
            new(new[]
            {
                ResourceProperty.Filename,
                ResourceProperty.DirectoryPath,
                ResourceProperty.CreatedAt,
                ResourceProperty.FileCreatedAt,
                ResourceProperty.FileModifiedAt,
                ResourceProperty.PlayedAt,
                ResourceProperty.MediaLibraryV2,
                ResourceProperty.MediaLibraryV2Multi,
                ResourceProperty.ParentResource,
                ResourceProperty.Source,
                ResourceProperty.HealthScore,
            }.ToDictionary(d => d, d => BuiltinPropertyMap[d]));

    /// <summary>
    /// Reserved property definitions (a filtered view over BuiltinPropertyMap).
    /// </summary>
    public static readonly ConcurrentDictionary<ReservedProperty, Bakabase.Abstractions.Models.Domain.Property>
        ReservedPropertyMap =
            new(new[]
            {
                ResourceProperty.Rating,
                ResourceProperty.Introduction,
                ResourceProperty.Cover,
                ResourceProperty.Name,
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
    /// Virtual property instances by type. Use PropertySystem.Property.GetVirtual() for public access.
    /// </summary>
    public static readonly ConcurrentDictionary<PropertyType, Bakabase.Abstractions.Models.Domain.Property>
        VirtualPropertyMap = new ConcurrentDictionary<PropertyType, Bakabase.Abstractions.Models.Domain.Property>(
            Descriptors.Select(d =>
                    new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Custom, 0, d.Type,
                        "Virtual property"))
                .ToDictionary(d => d.Type, d => d));
}
