using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Models;

public record CustomProperty : Bakabase.Abstractions.Models.Domain.CustomProperty
{
    public CustomPropertyType EnumType { get; set; }
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