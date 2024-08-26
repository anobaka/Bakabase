using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Abstractions.Models;

public record CustomProperty : Bakabase.Abstractions.Models.Domain.CustomProperty
{
    public CustomPropertyType EnumType => (CustomPropertyType) Type;
}


public record CustomProperty<TOptions> : CustomProperty
{
    private TOptions? _options;

    public new TOptions? Options
    {
        get => _options;
        set => base.Options = _options = value;
    }
}