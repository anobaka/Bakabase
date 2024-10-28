using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;

namespace Bakabase.Modules.Property.Components.Properties.Attachment;

public class AttachmentPropertyDescriptor : AbstractPropertyDescriptor<List<string>, List<string>>
{
    public override PropertyType Type => PropertyType.Attachment;

    protected override bool IsMatchInternal(List<string> dbValue, SearchOperation operation, object filterValue)
    {
        var fv = (filterValue as string)!;

        return operation switch
        {
            SearchOperation.Contains => dbValue.Any(x => x.Contains(fv, StringComparison.OrdinalIgnoreCase)),
            SearchOperation.NotContains => dbValue.All(x => !x.Contains(fv, StringComparison.OrdinalIgnoreCase)),
            _ => true
        };
    }

    public override Dictionary<SearchOperation, PropertySearchOperationOptions?>
        SearchOperations { get; } = new()
    {
        {SearchOperation.Contains, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.NotContains, new PropertySearchOperationOptions(PropertyType.SingleLineText)},
        {SearchOperation.IsNull, null},
        {SearchOperation.IsNotNull, null},
    };
}