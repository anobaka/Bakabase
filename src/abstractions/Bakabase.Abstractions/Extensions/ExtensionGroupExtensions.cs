using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Extensions;

public static class ExtensionGroupExtensions
{
    public static ExtensionGroupDbModel ToDbModel(this ExtensionGroup group)
    {
        return new ExtensionGroupDbModel(
            group.Id,
            group.Name,
            group.Extensions == null ? null : string.Join(InternalOptions.TextSeparator, group.Extensions)
        );
    }

    public static ExtensionGroup ToDomainModel(this ExtensionGroupDbModel dbModel)
    {
        return new ExtensionGroup
        {
            Id = dbModel.Id,
            Name = dbModel.Name,
            Extensions = string.IsNullOrEmpty(dbModel.Extensions)
                ? null
                : dbModel.Extensions.Split(InternalOptions.TextSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet()
        };
    }
}