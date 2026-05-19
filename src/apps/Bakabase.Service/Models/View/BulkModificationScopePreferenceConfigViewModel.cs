using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Models.View;

namespace Bakabase.Service.Models.View;

public record BulkModificationScopePreferenceConfigViewModel
{
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public PropertyViewModel? Property { get; set; }
    public PropertyValueScopePriority[]? Priorities { get; set; }
}
