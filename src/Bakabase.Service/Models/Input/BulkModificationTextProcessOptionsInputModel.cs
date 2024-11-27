using System.Collections.Generic;
using Bakabase.Abstractions.Components.TextProcessing;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Components;
using Bakabase.Modules.BulkModification.Components.Processors;

namespace Bakabase.Service.Models.Input;

public record BulkModificationTextProcessOptionsInputModel: IBulkModificationProcessOptionsInputModel
{
    public string? Value { get; init; }
    public string? Find { get; set; }
    public string? Replace { get; set; }
    public int? Position { get; set; }
    public bool? Reverse { get; set; }
    public bool? RemoveBefore { get; set; }
    public int? Count { get; set; }
    public int? Start { get; set; }
    public int? End { get; set; }

    public IBulkModificationProcessOptions ToDomainModel(Property property)
    {
        return new BulkModificationTextProcessOptions
        {

        };
    }
}