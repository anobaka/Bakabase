using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.BulkModification.Abstractions.Components;

public interface IBulkModificationLocalizer
{
    string DefaultName();
    string VariableIsNotFound(string variableName);
    string PropertyIsNotFound(PropertyPool? pool, int? id);
}