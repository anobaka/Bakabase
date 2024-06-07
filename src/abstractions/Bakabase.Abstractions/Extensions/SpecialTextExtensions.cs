using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Extensions;

public static class SpecialTextExtensions
{
    public static SpecialText ToDomainModel(this Models.Db.SpecialText dbModel)
    {
        return new SpecialText
        {
            Id = dbModel.Id,
            Value1 = dbModel.Value1,
            Value2 = dbModel.Value2,
            Type = dbModel.Type
        };
    }

    public static Models.Db.SpecialText ToDbModel(this SpecialText domainModel)
    {
        return new Models.Db.SpecialText
        {
            Id = domainModel.Id,
            Value1 = domainModel.Value1,
            Value2 = domainModel.Value2,
            Type = domainModel.Type
        };
    }

    public static Dictionary<SpecialTextType, List<SpecialText>> ToMap(this IEnumerable<SpecialText> texts)
    {
        return texts.GroupBy(x => x.Type).ToDictionary(d => d.Key, d => d.ToList());
    }
}