using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Components.Accessors;

namespace Bakabase.Modules.Property.Components.BuiltinProperty;

/// <summary>
/// Type-safe accessors for built-in properties.
/// Provides strongly-typed access to Internal and Reserved properties.
/// External code should use PropertySystem.Builtin for access.
/// </summary>
public static class BuiltinProperties
{
    /// <summary>
    /// Internal properties (system-managed, single value, no scope)
    /// </summary>
    internal static class Internal
    {
        /// <summary>
        /// Resource filename (SingleLineText)
        /// </summary>
        public static readonly TextPropertyAccessor Filename = new(ResourceProperty.Filename);

        /// <summary>
        /// Parent directory path (SingleLineText)
        /// </summary>
        public static readonly TextPropertyAccessor DirectoryPath = new(ResourceProperty.DirectoryPath);

        /// <summary>
        /// Record creation time (DateTime)
        /// </summary>
        public static readonly DateTimePropertyAccessor CreatedAt = new(ResourceProperty.CreatedAt);

        /// <summary>
        /// File system creation time (DateTime)
        /// </summary>
        public static readonly DateTimePropertyAccessor FileCreatedAt = new(ResourceProperty.FileCreatedAt);

        /// <summary>
        /// File system modification time (DateTime)
        /// </summary>
        public static readonly DateTimePropertyAccessor FileModifiedAt = new(ResourceProperty.FileModifiedAt);

        /// <summary>
        /// Last playback time (DateTime)
        /// </summary>
        public static readonly DateTimePropertyAccessor PlayedAt = new(ResourceProperty.PlayedAt);

        /// <summary>
        /// Media library binding (SingleChoice) - DEPRECATED, use MediaLibraryV2Multi
        /// This is a facade - all operations redirect to MediaLibraryV2Multi internally.
        /// </summary>
        [Obsolete("Use MediaLibraryV2Multi instead. Facade only.")]
        public static readonly SingleChoicePropertyAccessor MediaLibraryV2 = new(ResourceProperty.MediaLibraryV2);

        /// <summary>
        /// Media library binding with multiple choice support.
        /// New code should use this instead of MediaLibraryV2.
        /// Provides int-based API since media library IDs are integers.
        /// </summary>
        public static readonly MediaLibraryPropertyAccessor MediaLibraryV2Multi = new(ResourceProperty.MediaLibraryV2Multi);

        /// <summary>
        /// Parent resource link (SingleChoice)
        /// Provides int-based API since resource IDs are integers.
        /// </summary>
        public static readonly ParentResourcePropertyAccessor ParentResource = new(ResourceProperty.ParentResource);

        /// <summary>
        /// Category (SingleChoice) - DEPRECATED
        /// </summary>
        [Obsolete]
        public static readonly SingleChoicePropertyAccessor Category = new(ResourceProperty.Category);

        /// <summary>
        /// Media library (Multilevel) - DEPRECATED
        /// </summary>
        [Obsolete]
        public static readonly MultilevelPropertyAccessor MediaLibrary = new(ResourceProperty.MediaLibrary);
    }

    /// <summary>
    /// Reserved properties (system-defined, user-editable)
    /// </summary>
    internal static class Reserved
    {
        /// <summary>
        /// User rating (Rating/Decimal)
        /// </summary>
        public static readonly DecimalPropertyAccessor Rating = new(ResourceProperty.Rating);

        /// <summary>
        /// Resource description (MultilineText)
        /// </summary>
        public static readonly TextPropertyAccessor Introduction = new(ResourceProperty.Introduction);

        /// <summary>
        /// Cover image paths (Attachment/ListString)
        /// </summary>
        public static readonly AttachmentPropertyAccessor Cover = new(ResourceProperty.Cover);
    }

    /// <summary>
    /// Get property definition by ResourceProperty enum
    /// </summary>
    public static Bakabase.Abstractions.Models.Domain.Property Get(ResourceProperty prop) =>
        PropertyInternals.BuiltinPropertyMap[prop];

    /// <summary>
    /// Try get property definition by ResourceProperty enum
    /// </summary>
    public static Bakabase.Abstractions.Models.Domain.Property? TryGet(ResourceProperty prop) =>
        PropertyInternals.BuiltinPropertyMap.GetValueOrDefault(prop);
}
