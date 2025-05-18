using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Extensions;

public static class ExtensionGroupExtensions
{
    public static ExtensionGroupDbModel ToDbModel(this ExtensionGroup group)
    {
        return new ExtensionGroupDbModel(group.Id, group.Name,
            string.Join(InternalOptions.TextSeparator, group.Extensions));
    }

    public static ExtensionGroup ToDomainModel(this ExtensionGroupDbModel dbModel)
    {
        return new ExtensionGroup(dbModel.Id, dbModel.Name,
            dbModel.Extensions.Split(InternalOptions.TextSeparator, StringSplitOptions.RemoveEmptyEntries).ToHashSet());
    }
}