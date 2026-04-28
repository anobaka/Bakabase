using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Models.Domain;
using Bakabase.Modules.StandardValue.Extensions;

namespace Bakabase.Modules.Enhancer.Extensions;

public static class EnhancementExtensions
{
    public static Enhancement ToDomainModel(this Bakabase.Abstractions.Models.Db.EnhancementDbModel dbModel)
    {
        var rawValue = dbModel.Value?.DeserializeAsStandardValue(dbModel.ValueType);
        // Path-shape guard inside ResolveAll ensures non-path tokens (UUIDs etc.) pass through.
        if (rawValue is List<string> paths) rawValue = AppDataPaths.ResolveAll(paths);
        return new Enhancement
        {
            EnhancerId = dbModel.EnhancerId,
            Id = dbModel.Id,
            ResourceId = dbModel.ResourceId,
            Target = dbModel.Target,
            Value = rawValue,
            ValueType = dbModel.ValueType,
            PropertyPool = dbModel.PropertyPool,
            PropertyId = dbModel.PropertyId,
            DynamicTarget = dbModel.DynamicTarget,
            Key = dbModel.Key
        };
    }

    public static Bakabase.Abstractions.Models.Db.EnhancementDbModel ToDbModel(this Enhancement domainModel)
    {
        var valueForDb = domainModel.Value is List<string> paths
            ? AppDataPaths.RelativizeAll(paths)
            : domainModel.Value;
        var dbModel = new Bakabase.Abstractions.Models.Db.EnhancementDbModel
        {
            EnhancerId = domainModel.EnhancerId,
            Id = domainModel.Id,
            ResourceId = domainModel.ResourceId,
            Target = domainModel.Target,
            Value = valueForDb?.SerializeAsStandardValue(domainModel.ValueType),
            ValueType = domainModel.ValueType,
            PropertyPool = domainModel.PropertyPool,
            PropertyId = domainModel.PropertyId,
            DynamicTarget = domainModel.DynamicTarget
        };
        dbModel.FillKey();
        return dbModel;
    }
}