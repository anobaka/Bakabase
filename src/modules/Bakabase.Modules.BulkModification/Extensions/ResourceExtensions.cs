using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Extensions;

namespace Bakabase.Modules.BulkModification.Extensions;

public static class ResourceExtensions
{
    /// <summary>
    /// todo: Don't know where to put this method
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public static List<ResourceDiff> Compare(this Dictionary<int, Dictionary<int, Resource.Property>>? a,
        Dictionary<int, Dictionary<int, Resource.Property>>? b)
    {
        var diffs = new List<ResourceDiff>();

        var comparePools = (a?.Keys.ToArray() ?? []).Concat(b?.Keys.ToArray() ?? [])
            .ToHashSet();

        foreach (var pp in comparePools)
        {
            var aPvs = a?.GetValueOrDefault(pp);
            var bPvs = b?.GetValueOrDefault(pp);

            var comparePropertyIds = (aPvs?.Keys.ToArray() ?? []).Concat(bPvs?.Keys.ToArray() ?? []).ToHashSet();

            foreach (var propertyId in comparePropertyIds)
            {
                var aPv = aPvs?.GetValueOrDefault(propertyId)?.Values?
                    .FirstOrDefault(x => x.Scope == (int) PropertyValueScope.Manual)?.BizValue;
                var bPv = bPvs?.GetValueOrDefault(propertyId)?.Values
                    ?.FirstOrDefault(x => x.Scope == (int) PropertyValueScope.Manual)?.BizValue;

                var bizValueType = (aPvs?.GetValueOrDefault(propertyId) ?? bPvs?.GetValueOrDefault(propertyId))!
                    .BizValueType;

                var stdHandler = StandardValueInternals.HandlerMap[bizValueType];
                if (!stdHandler.Compare(aPv, bPv))
                {
                    diffs.Add(new ResourceDiff
                    {
                        PropertyPool = (PropertyPool) pp,
                        PropertyId = propertyId,
                        Value1 = aPv,
                        SerializedValue1 = aPv?.SerializeAsStandardValue(bizValueType),
                        Value2 = bPv,
                        SerializedValue2 = bPv?.SerializeAsStandardValue(bizValueType)
                    });
                }
            }
        }

        return diffs.ToList()!;
    }
}