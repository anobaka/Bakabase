using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Components.Accessors;

namespace Bakabase.Modules.Property.Components.BuiltinProperty;

/// <summary>
/// Built-in property definitions, addressed by ResourceProperty.
/// External code should use PropertySystem.Builtin for access.
/// </summary>
public static class BuiltinProperties
{
    /// <summary>
    /// Media library binding with multiple choice support.
    /// Provides int-based API since media library IDs are integers.
    /// </summary>
    public static readonly MediaLibraryPropertyAccessor MediaLibraryV2Multi =
        new(ResourceProperty.MediaLibraryV2Multi);

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
