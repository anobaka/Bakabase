using Bakabase.Abstractions.Components.Property;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Abstractions.Models;

public record BulkModificationProcess : IPropertyKeyHolder
{
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public Bakabase.Abstractions.Models.Domain.Property Property { get; set; } = null!;
    public List<BulkModificationProcessStep>? Steps { get; set; } = [];
}

public record BulkModificationProcess<TOperation, TOptions> : BulkModificationProcess
    where TOptions : class, IBulkModificationProcessOptions where TOperation : Enum
{
    public new List<BulkModificationProcessStep<TOperation, TOptions>>? Steps
    {
        get => base.Steps?.Cast<BulkModificationProcessStep<TOperation, TOptions>>().ToList();
        set => base.Steps = value?.Cast<BulkModificationProcessStep>().ToList();
    }
}