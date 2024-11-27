using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Components;
using Microsoft.Extensions.Localization;

namespace Bakabase.Modules.BulkModification.Components;

internal class BulkModificationLocalizer(IStringLocalizer<BulkModificationResource> localizer) : IBulkModificationLocalizer
{
    public string DefaultName() => localizer[nameof(DefaultName)];
}