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

    /// <summary>External work identities, independent of where the resource's files came from.</summary>
    public List<ResourceExternalIdentity>? ExternalIdentities { get; set; }

    public string? FileName => string.IsNullOrEmpty(Path) ? null : System.IO.Path.GetFileName(Path);

    public string? Directory => string.IsNullOrEmpty(Path) ? null : System.IO.Path.GetDirectoryName(Path).StandardizePath()!;

    public string? Path { get; set; }

    /// <summary>
    /// Whether this resource currently has local files. False means the resource is known
    /// to Bakabase but not materialized on disk yet — an uninstalled Steam game, a work the
    /// user intends to acquire. Prefer this over inspecting <see cref="Path"/> directly so
    /// the rule lives in one place.
    /// </summary>
    public bool HasLocalPath => !string.IsNullOrEmpty(Path);

    private string? _displayName;

    public string? DisplayName
    {
        get => _displayName ?? FileName;
        set => _displayName = value;
    }

    public int? ParentId { get; set; }
    public bool HasChildren => Tags.Contains(ResourceTag.IsParent);
    public bool IsFile { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
    public DateTime FileCreatedAt { get; set; }
    public DateTime FileModifiedAt { get; set; }
    public IReadOnlySet<ResourceTag> Tags { get; set; } = FrozenSet<ResourceTag>.Empty;
    public Resource? Parent { get; set; }
    /// <summary>
    /// ResourcePropertyType - PropertyId - Property
    /// </summary>
    public Dictionary<int, Dictionary<int, Property>>? Properties { get; set; }

    /// <summary>
    /// Per-(propertyPool, propertyId) value scope preferences for this resource. Most granular layer
    /// in the resolution chain (per-resource → profile → global). Populated alongside Properties.
    /// </summary>
    public List<PropertyValueScopePreference>? ScopePreferences { get; set; }

    public bool Pinned => Tags.Contains(ResourceTag.Pinned);
    public DateTime? PlayedAt { get; set; }

    /// <summary>
    /// Aggregated health score (max-priority then min-score across enabled
    /// profiles). Populated by the resource-loading layer from
    /// <see cref="Bakabase.Abstractions.Services.IResourceHealthScoreReader"/>
    /// when the HealthScore module is wired in. Null when no profile has scored
    /// the resource.
    /// </summary>
    public decimal? HealthScore { get; set; }

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
    /// Per-origin readiness states for cover, playable item, and metadata data.
    /// Populated at runtime by providers; not persisted.
    /// </summary>
    public List<ResourceDataState>? DataStates { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public ResourceFileSystemCache? Cache { get; set; }

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
        /// <summary>
        /// Scope order from this property's effective resource profile. Null/empty uses the global
        /// order; a per-resource ScopePreference still takes precedence. This is derived response
        /// metadata, not a saved per-resource override.
        /// </summary>
        public PropertyValueScope[]? ProfileScopePriority { get; set; }

        public record PropertyValue(
            int Scope,
            object? Value,
            object? BizValue,
            object? AliasAppliedBizValue)
        {
            public int Scope { get; set; } = Scope;
            public object? Value { get; set; } = Value;

            /// <summary>
            /// Null means the descriptor could not interpret the stored db value — it points at a
            /// choice / tag / node that no longer exists. Falling back to <see cref="Value"/> here
            /// would leak the raw id to the UI (and, for Tags and Multilevel, a value of the wrong
            /// shape entirely, since their biz type is not ListString), defeating the miss
            /// behavior every read path is supposed to share. Callers treat null as absent.
            /// </summary>
            public object? BizValue { get; set; } = BizValue;

            /// <summary>
            /// Alias application is optional, so falling back to <see cref="BizValue"/> is correct —
            /// falling back to <see cref="Value"/> is not, for the reason above.
            /// </summary>
            public object? AliasAppliedBizValue { get; set; } = AliasAppliedBizValue ?? BizValue;

            public bool IsManuallySet => Scope == (int)PropertyValueScope.Manual;
        }
    }

    [Obsolete]
    public string? MediaLibraryName { get; set; }
    [Obsolete]
    public string? MediaLibraryColor { get; set; }

    public List<MediaLibraryInfo>? MediaLibraries { get; set; }

    /// <summary>
    /// Which collections this belongs to — written-down memberships and rule matches alike. Loaded
    /// only when asked for (<c>ResourceAdditionalItem.CollectionName</c>).
    /// </summary>
    public List<CollectionInfo>? Collections { get; set; }

    public record MediaLibraryInfo(int Id, string Name, string? Color);

    public record CollectionInfo(int Id, string Name, string? Color);
}
