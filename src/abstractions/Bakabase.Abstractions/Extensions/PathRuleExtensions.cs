using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Abstractions.Extensions;

public static class PathRuleExtensions
{
    public static PathRuleDbModel ToDbModel(this PathRule model)
    {
        return new PathRuleDbModel
        {
            Id = model.Id,
            Path = model.Path,
            MarksJson = JsonConvert.SerializeObject(model.Marks),
            CreateDt = model.CreateDt,
            UpdateDt = model.UpdateDt
        };
    }

    public static PathRule ToDomainModel(this PathRuleDbModel dbModel)
    {
        var domain = new PathRule
        {
            Id = dbModel.Id,
            Path = dbModel.Path,
            CreateDt = dbModel.CreateDt,
            UpdateDt = dbModel.UpdateDt,
            Marks = new List<PathMark>()
        };

        if (!string.IsNullOrEmpty(dbModel.MarksJson))
        {
            try
            {
                domain.Marks = JsonConvert.DeserializeObject<List<PathMark>>(dbModel.MarksJson) ?? new List<PathMark>();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        return domain;
    }
}
