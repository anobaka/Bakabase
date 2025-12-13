using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Extensions;

public static class PathRuleQueueItemExtensions
{
    public static PathRuleQueueItemDbModel ToDbModel(this PathRuleQueueItem model)
    {
        return new PathRuleQueueItemDbModel
        {
            Id = model.Id,
            Path = model.Path,
            Action = model.Action,
            RuleId = model.RuleId,
            CreateDt = model.CreateDt,
            Status = model.Status,
            Error = model.Error
        };
    }

    public static PathRuleQueueItem ToDomainModel(this PathRuleQueueItemDbModel dbModel)
    {
        return new PathRuleQueueItem
        {
            Id = dbModel.Id,
            Path = dbModel.Path,
            Action = dbModel.Action,
            RuleId = dbModel.RuleId,
            CreateDt = dbModel.CreateDt,
            Status = dbModel.Status,
            Error = dbModel.Error
        };
    }
}
