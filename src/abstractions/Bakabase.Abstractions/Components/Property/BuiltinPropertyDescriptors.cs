using System.Collections.Concurrent;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Components.Property;

public record BuiltinPropertyDescriptors
{
    public static ConcurrentDictionary<ResourceProperty, Models.Domain.Property> DescriptorMap { get; } =
        new(
            new[]
            {
                new Models.Domain.Property(ResourcePropertyType.Internal, (int) ResourceProperty.FileName,
                    StandardValueType.String, StandardValueType.String),
                new Models.Domain.Property(ResourcePropertyType.Internal, (int) ResourceProperty.DirectoryPath,
                    StandardValueType.String, StandardValueType.String),
                new Models.Domain.Property(ResourcePropertyType.Internal, (int) ResourceProperty.CreatedAt,
                    StandardValueType.DateTime, StandardValueType.DateTime),
                new Models.Domain.Property(ResourcePropertyType.Internal, (int) ResourceProperty.FileCreatedAt,
                    StandardValueType.DateTime, StandardValueType.DateTime),
                new Models.Domain.Property(ResourcePropertyType.Internal, (int) ResourceProperty.FileModifiedAt,
                    StandardValueType.DateTime, StandardValueType.DateTime),
                // this is a hardcode for list<int>
                new Models.Domain.Property(ResourcePropertyType.Internal, (int) ResourceProperty.MediaLibrary,
                    StandardValueType.ListString, StandardValueType.ListListString),

                new Models.Domain.Property(ResourcePropertyType.Reserved, (int) ResourceProperty.Rating,
                    StandardValueType.Decimal, StandardValueType.Decimal),
                new Models.Domain.Property(ResourcePropertyType.Reserved, (int) ResourceProperty.Introduction,
                    StandardValueType.String, StandardValueType.String),
            }.ToDictionary(d => d.EnumId, d => d));
}