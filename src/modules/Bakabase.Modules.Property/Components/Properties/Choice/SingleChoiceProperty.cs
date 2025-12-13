using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Extensions;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.Property.Components.Properties.Choice;

public record SingleChoicePropertyOptions: ChoicePropertyOptions<string>;

public class SingleChoicePropertyDescriptor : AbstractPropertyDescriptor<SingleChoicePropertyOptions, string, string>
{
    public override PropertyType Type => Bakabase.Abstractions.Models.Domain.Constants.PropertyType.SingleChoice;

    protected override (object DbValue, SearchOperation Operation)? BuildSearchFilterByKeywordInternal(
        Bakabase.Abstractions.Models.Domain.Property property, string keyword)
    {
        var options = property.Options as SingleChoicePropertyOptions;
        var ids = options?.Choices?.Where(c => c.Label.Contains(keyword)).Select(x => x.Value).ToList();
        return ids?.Any() == true ? (ids, SearchOperation.In) : null;
    }

    protected override bool IsMatchInternal(string dbValue, SearchOperation operation, object filterValue)
    {
        switch (operation)
        {
            case SearchOperation.Equals:
            case SearchOperation.NotEquals:
            {
                var fv = (filterValue as string)!;
                return operation == SearchOperation.Equals ? dbValue == fv : dbValue != fv;
            }
            case SearchOperation.In:
            case SearchOperation.NotIn:
            {
                var fv = (filterValue as List<string>)!;
                return operation == SearchOperation.In ? fv.Contains(dbValue) : !fv.Contains(dbValue);
            }
            default:
                return true;
        }
    }

    /// <summary>
    /// 重写索引搜索以处理 In/NotIn 操作（filter 值为 List&lt;string&gt;）
    /// </summary>
    public override HashSet<int>? SearchIndex(
        SearchOperation operation,
        object? filterDbValue,
        IReadOnlyDictionary<string, HashSet<int>>? valueIndex,
        IReadOnlyList<KeyValuePair<IComparable, HashSet<int>>>? rangeIndex,
        IReadOnlyCollection<int> allResourceIds)
    {
        // In/NotIn 操作的 filter 值是 List<string>
        if (operation is SearchOperation.In or SearchOperation.NotIn && filterDbValue is List<string> list)
        {
            return SearchWithList(operation, list, valueIndex, allResourceIds);
        }

        // 其他操作使用默认实现
        return base.SearchIndex(operation, filterDbValue, valueIndex, rangeIndex, allResourceIds);
    }

    private static HashSet<int> SearchWithList(
        SearchOperation operation,
        List<string> filterValues,
        IReadOnlyDictionary<string, HashSet<int>>? valueIndex,
        IReadOnlyCollection<int> allResourceIds)
    {
        if (valueIndex == null || filterValues.Count == 0)
        {
            return operation == SearchOperation.NotIn
                ? new HashSet<int>(allResourceIds)
                : new HashSet<int>();
        }

        var matchingSets = filterValues
            .Select(v => valueIndex.GetValueOrDefault(Normalize(v)))
            .ToList();

        return operation switch
        {
            // In: 资源的值在 filter 列表中 → 并集
            SearchOperation.In => UnionAll(matchingSets),

            // NotIn: 资源的值不在 filter 列表中 → 取反
            SearchOperation.NotIn => Negate(UnionAll(matchingSets), allResourceIds),

            _ => new HashSet<int>()
        };
    }

    public override Dictionary<SearchOperation, PropertySearchOperationOptions?>
        SearchOperations { get; } = new()
    {
        {SearchOperation.Equals, new PropertySearchOperationOptions(PropertyType.SingleChoice)},
        {SearchOperation.NotEquals, new PropertySearchOperationOptions(PropertyType.SingleChoice)},
        {SearchOperation.IsNull, null}, {SearchOperation.IsNotNull, null},
        {
            SearchOperation.In,
            new PropertySearchOperationOptions(PropertyType.MultipleChoice, ConvertToMultipleChoiceForSearchOperation)
        },
        {
            SearchOperation.NotIn,
            new PropertySearchOperationOptions(PropertyType.MultipleChoice, ConvertToMultipleChoiceForSearchOperation)
        },
    };

    private static Bakabase.Abstractions.Models.Domain.Property ConvertToMultipleChoiceForSearchOperation(
        Bakabase.Abstractions.Models.Domain.Property p)
    {
        var options = (p.Options as SingleChoicePropertyOptions);
        return new Bakabase.Abstractions.Models.Domain.Property(p.Pool, p.Id, PropertyType.MultipleChoice, p.Name,
            new MultipleChoicePropertyOptions
            {
                Choices = options?.Choices,
                // AllowAddingNewDataDynamically = options?.AllowAddingNewDataDynamically ?? false,
                DefaultValue = string.IsNullOrEmpty(options?.DefaultValue) ? null : [options.DefaultValue]
            }, p.Order);
    }

    protected override (string? DbValue, bool PropertyChanged) PrepareDbValueInternal(
        Bakabase.Abstractions.Models.Domain.Property property, string bizValue)
    {
        bizValue = bizValue.Trim();
        if (!string.IsNullOrEmpty(bizValue))
        {
            property.Options ??= new SingleChoicePropertyOptions();
            var options = (property.Options as SingleChoicePropertyOptions)!;
            var propertyChanged = options.AddChoices(true, [bizValue], null);
            var stringValue = options.Choices?.Find(x => x.Label == bizValue)?.Value;
            var nv = new StringValueBuilder(stringValue).Value;
            return (nv, propertyChanged);
        }

        return (null, false);
    }

    protected override string? GetBizValueInternal(Bakabase.Abstractions.Models.Domain.Property property, string value)
    {
        var options = (property.Options as SingleChoicePropertyOptions)!;
        return options?.Choices?.FirstOrDefault(c => c.Value == value)?.Label;
    }
}