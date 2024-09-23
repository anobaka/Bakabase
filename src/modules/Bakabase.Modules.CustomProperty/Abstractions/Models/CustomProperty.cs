using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Abstractions.Models;

public record CustomProperty : Bakabase.Abstractions.Models.Domain.CustomProperty
{
    public CustomPropertyType EnumType => (CustomPropertyType) Type;
}

public record CustomProperty<TOptions> : CustomProperty where TOptions : class
{
    public new TOptions? Options
    {
        get => base.Options as TOptions;
        set => base.Options = value;
    }
}