using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models;
using Bakabase.Modules.Property.Abstractions.Models.Domain;
using Bakabase.Modules.Property.Extensions;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.Property.Components.Properties.Multilevel;

public class MultilevelPropertyDescriptor : AbstractPropertyDescriptor<MultilevelPropertyOptions, List<string>, List<List<string>>>
{
    public override PropertyType Type => PropertyType.Multilevel;

    protected override (object DbValue, SearchOperation Operation)? BuildSearchFilterByKeywordInternal(Bakabase.Abstractions.Models.Domain.Property property, string keyword)
    {
        var options = property.Options as MultilevelPropertyOptions;
        var nodes = options?.Data?.Select(d => d.FindNode(x => x.Label.Contains(keyword)))
            .OfType<MultilevelDataOptions>().ToList();
        if (nodes?.Any() != true)
        {
            return null;
        }

        var ids = nodes.ExtractValues(false).ToHashSet();
        return (ids, SearchOperation.In);
    }

    protected override bool IsMatchInternal(List<string> dbValue, SearchOperation operation, object filterValue)
    {
        var fv = (List<string>) filterValue;
        return operation switch
        {
            SearchOperation.Contains => fv.All(dbValue.Contains),
            SearchOperation.NotContains => fv.All(v => !dbValue.Contains(v)),
            SearchOperation.In => dbValue.All(fv.Contains),
            _ => true
        };
    }

    public override Dictionary<SearchOperation, PropertySearchOperationOptions?> SearchOperations { get; } = new()
    {
        {SearchOperation.Contains, new PropertySearchOperationOptions(PropertyType.Multilevel)},
        {SearchOperation.NotContains, new PropertySearchOperationOptions(PropertyType.Multilevel)},
        {SearchOperation.In, new PropertySearchOperationOptions(PropertyType.Multilevel)},
        {SearchOperation.IsNull, null}, {SearchOperation.IsNotNull, null}
    };

    protected override (List<string>? DbValue, bool PropertyChanged) PrepareDbValueInternal(Bakabase.Abstractions.Models.Domain.Property property, List<List<string>> bizValue)
    {
        if (bizValue.Any())
        {
            bizValue.TrimAll();
            var options =
                ((property.Options ??= new MultilevelPropertyOptions() {AllowAddingNewDataDynamically = true}) as
                    MultilevelPropertyOptions)!;
            if (options.HasSingleValue)
            {
                bizValue = bizValue.Take(1).ToList();
            }

            var propertyChanged = options.AddBranchOptions(bizValue);

            var branches = options.Data ?? [];
            var values = branches.FindValuesByLabelChains(bizValue).OfType<string>().ToList();

            return (values.Any() ? values : null, propertyChanged);
        }

        return (null, false);
    }

    protected override List<List<string>>? GetBizValueInternal(Bakabase.Abstractions.Models.Domain.Property property, List<string> value)
    {
        var data = new List<List<string>>();
        var options = property.Options as MultilevelPropertyOptions;
        foreach (var v in value)
        {
            if (options?.Data != null)
            {
                foreach (var d in options.Data)
                {
                    var chain = d.FindLabelChain(v);
                    if (chain != null)
                    {
                        data.Add(chain.ToList());
                    }
                }
            }
            else
            {
                data.Add([v]);
            }
        }

        return data;
    }
}