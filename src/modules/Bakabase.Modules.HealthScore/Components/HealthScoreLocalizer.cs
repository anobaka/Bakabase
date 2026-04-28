using Bakabase.Modules.HealthScore.Abstractions.Components;
using Microsoft.Extensions.Localization;

namespace Bakabase.Modules.HealthScore.Components;

internal class HealthScoreLocalizer(IStringLocalizer<HealthScoreResource> localizer) : IHealthScoreLocalizer
{
    public string DefaultProfileName() => localizer[nameof(DefaultProfileName)];
    public string PredicateNotFound(string predicateId) => localizer[nameof(PredicateNotFound), predicateId];
}
