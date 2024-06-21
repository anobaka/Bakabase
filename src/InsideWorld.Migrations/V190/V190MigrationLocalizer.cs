using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.Localization;

namespace InsideWorld.Migrations.V190;

public class V190MigrationLocalizer
{
    private readonly IStringLocalizer<SharedResource> _localizer;

    public V190MigrationLocalizer(IStringLocalizer<SharedResource> localizer)
    {
        _localizer = localizer;
    }

    public string DefaultPropertyName(LegacyResourceProperty property, string? subProperty)
    {
        var key = $"Migration_V190_DefaultPropertyName_{property.ToString()}";
        if (property == LegacyResourceProperty.Volume && !string.IsNullOrEmpty(subProperty))
        {
            key = $"{key}_{subProperty}";
        }

        var name = _localizer[key].ToString();
        if (!string.IsNullOrEmpty(subProperty) && property == LegacyResourceProperty.CustomProperty)
        {
            name += $"_{subProperty}";
        }

        return name;
    }

    public string Language(ResourceLanguage language)
    {
        return _localizer[language.ToString()];
    }
}