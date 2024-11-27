using Bakabase.Modules.BulkModification.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Components;

public interface IBulkModificationProcessOptionsInputModel
{
    IBulkModificationProcessOptions ToDomainModel(Bakabase.Abstractions.Models.Domain.Property property);
}