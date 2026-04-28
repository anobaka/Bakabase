namespace Bakabase.Modules.HealthScore.Abstractions.Components;

public interface IHealthScoreLocalizer
{
    string DefaultProfileName();
    string PredicateNotFound(string predicateId);
}
