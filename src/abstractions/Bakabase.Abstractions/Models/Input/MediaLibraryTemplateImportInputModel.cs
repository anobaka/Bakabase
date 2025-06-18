using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.View;

namespace Bakabase.Abstractions.Models.Input;

public record MediaLibraryTemplateImportInputModel
{
    public string? Name { get; set; }
    public string ShareCode { get; set; } = null!;

    /// <summary>
    /// Key is index in <see cref="MediaLibraryTemplateImportConfigurationViewModel.UniqueCustomProperties"/>
    /// </summary>
    public Dictionary<int, TCustomPropertyConversion>? CustomPropertyConversionsMap { get; set; }

    /// <summary>
    /// Key is index in <see cref="MediaLibraryTemplateImportConfigurationViewModel.UniqueExtensionGroups"/>
    /// </summary>
    public Dictionary<int, TExtensionGroupConversion>? ExtensionGroupConversionsMap { get; set; }

    public bool AutomaticallyCreateMissingData { get; set; }

    public record TCustomPropertyConversion
    {
        public PropertyPool ToPropertyPool { get; set; }
        public int ToPropertyId { get; set; }
    }

    public record TExtensionGroupConversion
    {
        public int ToExtensionGroupId { get; set; }
    }
}