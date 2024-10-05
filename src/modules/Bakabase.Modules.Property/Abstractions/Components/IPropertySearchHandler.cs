using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;

namespace Bakabase.Modules.Property.Abstractions.Components;

public interface IPropertySearchHandler
{
    PropertyType Type { get; }
    // object? BuildBizValue(object? filterValue, SearchOperation searchOperation, object? propertyOptions);
    bool IsMatch(object? dbValue, SearchOperation operation, object? filterValue);

    Dictionary<SearchOperation, PropertySearchOperationOptions?> SearchOperations { get; }

    ResourceSearchFilter? BuildSearchFilterByKeyword(Bakabase.Abstractions.Models.Domain.Property property,
        string keyword);
}