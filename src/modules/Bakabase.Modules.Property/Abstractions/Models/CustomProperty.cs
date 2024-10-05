using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Abstractions.Models;

public record CustomProperty<TOptions> : Bakabase.Abstractions.Models.Domain.CustomProperty where TOptions : class
{
    public new TOptions? Options
    {
        get => base.Options as TOptions;
        set => base.Options = value;
    }
}