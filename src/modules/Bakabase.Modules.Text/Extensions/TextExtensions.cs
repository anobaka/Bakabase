using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Text.Extensions;

public static class TextExtensions
{
    public static TextTypeDescriptor ToDescriptor(this Abstractions.Models.Db.TextType dbModel, int entryCount) =>
        new()
        {
            Id = dbModel.Id,
            Name = dbModel.Name,
            WellKnown = dbModel.WellKnown,
            Shape = dbModel.Shape,
            Description = dbModel.Description,
            CreatedAt = dbModel.CreatedAt,
            EntryCount = entryCount
        };

    public static TextEntryValue ToDomainModel(this Abstractions.Models.Db.TextEntry dbModel) =>
        new()
        {
            Id = dbModel.Id,
            TypeId = dbModel.TypeId,
            Value1 = dbModel.Value1,
            Value2 = dbModel.Value2
        };
}
