using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Extensions;

namespace Bakabase.Modules.Enhancer.Extensions;

/// <summary>
/// DB ↔ in-memory boundary. The target's PropertyType (read from the EnhancerTarget attribute,
/// not the runtime property mapping) decides whether the List&lt;string&gt; value is paths or
/// not. Other List&lt;string&gt; targets carry UUIDs or labels containing '/' (e.g. DLsite tag
/// "人外娘/モンス夕一娘") and must NOT go through AppData transforms; the original #1082 bug
/// was a blind <c>ResolveAll</c> at this layer. AppData transforms are no-ops for non-AppData
/// absolute paths, so user-disk files (e.g. existing covers found by BakabaseEnhancer) pass
/// through unchanged.
/// </summary>
public static class EnhancementExtensions
{
    public static Enhancement ToDomainModel(this Bakabase.Abstractions.Models.Db.EnhancementDbModel dbModel,
        PropertyType? targetPropertyType)
    {
        var rawValue = dbModel.Value?.DeserializeAsStandardValue(dbModel.ValueType);
        if (targetPropertyType == PropertyType.Attachment && rawValue is List<string> paths)
        {
            rawValue = AppDataPaths.ResolveAll(paths);
        }
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

    public static Bakabase.Abstractions.Models.Db.EnhancementDbModel ToDbModel(this Enhancement domainModel,
        PropertyType? targetPropertyType)
    {
        var valueForDb = targetPropertyType == PropertyType.Attachment && domainModel.Value is List<string> paths
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
