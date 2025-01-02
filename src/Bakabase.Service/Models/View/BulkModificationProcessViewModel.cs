using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.Property.Models.View;

namespace Bakabase.Service.Models.View;

public record BulkModificationProcessViewModel
{
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public PropertyViewModel Property { get; set; } = null!;
    public List<BulkModificationProcessStepViewModel>? Steps { get; set; }
}