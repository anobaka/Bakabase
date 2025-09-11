using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record ScopePropertyKey(PropertyPool Pool, int Id, PropertyValueScope Scope) : PropertyKey(Pool, Id)
{
    public PropertyValueScope Scope { get; set; } = Scope;
}