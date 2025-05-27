using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Services;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Input;

public record MediaLibraryTemplateImportInputModel
{
    public string ShareCode { get; set; } = null!;
    public Dictionary<int, TCustomPropertyConversion>? CustomPropertyConversionsMap { get; set; }
    public Dictionary<int, TExtensionGroupConversion>? ExtensionGroupConversionsMap { get; set; }

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