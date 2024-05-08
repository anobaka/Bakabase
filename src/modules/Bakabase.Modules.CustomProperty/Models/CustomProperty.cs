using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Models;

public record CustomProperty : Bakabase.Abstractions.Models.Domain.CustomProperty
{
    public CustomPropertyType EnumType { get; set; }
}


public record CustomProperty<T> : CustomProperty
{
    private T? _options;

    public new T? Options
    {
        get => _options;
        set => base.Options = _options = value;
    }
}