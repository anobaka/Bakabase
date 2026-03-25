using Bakabase.InsideWorld.Models.Constants;
using System.Collections.Frozen;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record Resource
{
    public int Id { get; set; }

    [Obsolete]
    public int MediaLibraryId { get; set; }

    public ResourceStatus Status { get; set; } = ResourceStatus.Active;

    /// <summary>
    /// Source links for this resource. A resource can be discovered by multiple sources.
    /// </summary>
    public List<ResourceSourceLink>? SourceLinks { get; set; }

    public string? FileName => string.IsNullOrEmpty(Path) ? null : System.IO.Path.GetFileName(Path);

    public string? Directory => string.IsNullOrEmpty(Path) ? null : System.IO.Path.GetDirectoryName(Path).StandardizePath()!;

    public string? Path { get; set; }

    private string? _displayName;

    public string? DisplayName
    {
        get => _displayName ?? FileName;
        set => _displayName = value;
    }

    public int? ParentId { get; set; }
    public bool HasChildren => Tags.Contains(ResourceTag.IsParent);
    public bool IsFile { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public DateTime FileCreatedAt { get; set; }
    public DateTime FileModifiedAt { get; set; }
    public List<string>? CoverPaths { get; set; }
    public IReadOnlySet<ResourceTag> Tags { get; set; } = FrozenSet<ResourceTag>.Empty;
    public Resource? Parent { get; set; }
    /// <summary>
    /// ResourcePropertyType - PropertyId - Property
    /// </summary>
    public Dictionary<int, Dictionary<int, Property>>? Properties { get; set; }

    public bool Pinned => Tags.Contains(ResourceTag.Pinned);
    public DateTime? PlayedAt { get; set; }

    /// <summary>
    /// Final resolved cover paths for this resource, populated by service layer using priority:
    /// 1. User-set covers (ReservedProperty.Cover, scope=Manual)
    /// 2. External source local covers (SourceLink.LocalCoverPaths)
    /// 3. Enhancer covers (scope=XxxEnhancer)
    /// 4. FileSystem auto-discovered covers (from cache)
    /// </summary>
    public List<string>? Covers { get; set; }

    /// <summary>
    /// Final resolved playable items for this resource, aggregated from all sources.
    /// </summary>
    public List<PlayableItem>? PlayableItems { get; set; }

    /// <summary>
    /// Whether there are more FileSystem playable items beyond what's shown (due to trimming limits).
    /// </summary>
    public bool HasMoreFileSystemPlayableItems { get; set; }

    /// <summary>
    /// Whether covers have been resolved and are ready.
    /// </summary>
    public bool CoversReady { get; set; }

    /// <summary>
    /// Whether playable items have been resolved and are ready.
    /// </summary>
    public bool PlayableItemsReady { get; set; }

    public ResourceCache? Cache { get; set; }

    public record Property(
        string? Name,
        PropertyType Type,
        List<Property.PropertyValue>? Values,
        bool Visible = false,
        int Order = 0)
    {
        public string? Name { get; set; } = Name;
        public List<PropertyValue>? Values { get; set; } = Values;
        public PropertyType Type { get; set; } = Type;
        public StandardValueType DbValueType => PropertyTypeValueTypes.GetDbValueType(Type);
        public StandardValueType BizValueType => PropertyTypeValueTypes.GetBizValueType(Type);
        public bool Visible { get; set; } = Visible;
        public int Order { get; set; } = Order;

        public record PropertyValue(
            int Scope,
            object? Value,
            object? BizValue,
            object? AliasAppliedBizValue)
        {
            public int Scope { get; set; } = Scope;
            public object? Value { get; set; } = Value;
            public object? BizValue { get; set; } = BizValue ?? Value;
            public object? AliasAppliedBizValue { get; set; } = AliasAppliedBizValue ?? BizValue ?? Value;

            public bool IsManuallySet => Scope == (int)PropertyValueScope.Manual;
        }
    }

    [Obsolete]
    public string? MediaLibraryName { get; set; }
    [Obsolete]
    public string? MediaLibraryColor { get; set; }

    public List<MediaLibraryInfo>? MediaLibraries { get; set; }

    public record MediaLibraryInfo(int Id, string Name, string? Color);
}