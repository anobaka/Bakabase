using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;

public record DownloaderDefinition
{
    /// <summary>
    /// The platform this task type belongs to
    /// </summary>
    public ThirdPartyId ThirdPartyId { get; set; }

    /// <summary>
    /// Unique identifier for this task type within the platform
    /// </summary>
    public int TaskType { get; set; }

    // public Type TaskTypeEnumType { get; set; } = null!;

    public object EnumTaskType { get; set; } = null!;

    /// <summary>
    /// Display name for the task type
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Detailed description of what this task type does
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The downloader type that handles this task type
    /// </summary>
    public Type DownloaderType { get; set; } = null!;

    public Type HelperType { get; set; } = null!;

    public string DefaultConvention { get; set; } = null!;
    public List<Field> NamingFields { get; set; } = [];
    // public Type NamingFieldEnumType { get; set; } = null!;

    public record Field
    {
        public object EnumValue { get; set; } = null!;
        public string Key { get; set; } = null!;
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Example { get; set; }
    }
}