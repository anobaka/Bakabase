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

    public string DefaultPropertyName(ResourceProperty property, string? subProperty)
    {
        var key = $"Migration_V190_DefaultPropertyName_{property}";
        if (!string.IsNullOrEmpty(subProperty))
        {
            key += $"_{subProperty}";
        }

        return _localizer[key];
    }
}