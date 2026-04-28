using System.Text.Json;

namespace Bakabase.Modules.HealthScore.Components;

/// <summary>
/// Pluggable filesystem-state predicate for use as a leaf in a
/// <see cref="Models.ResourceMatcher"/>. Implementations are auto-registered
/// via DI and looked up by <see cref="Id"/>.
///
/// Parameters arrive as a JSON string — the frontend serializes its parameter
/// object before sending, and the predicate (via the generic base) parses
/// back to its concrete <see cref="ParametersType"/> at evaluation time. We
/// avoid <see cref="JsonElement"/> entirely so the request body buffer
/// lifetime never bites us.
/// </summary>
public interface IFilePredicate
{
    /// <summary>Stable identifier used in DB and over the wire.</summary>
    string Id { get; }

    /// <summary>Localization key for the display name (predicate.{Id}.name).</summary>
    string DisplayNameKey => $"predicate.{Id}.name";

    /// <summary>POCO type the parameters JSON deserializes into. Use object for parameter-less predicates.</summary>
    Type ParametersType { get; }

    bool Evaluate(ResourceFsSnapshot snapshot, string? parametersJson);
}

/// <summary>
/// Convenience base class with strongly-typed parameter parsing.
/// </summary>
public abstract class FilePredicate<TParameters> : IFilePredicate
    where TParameters : class
{
    public abstract string Id { get; }
    public Type ParametersType => typeof(TParameters);

    public virtual string DisplayNameKey => $"predicate.{Id}.name";

    public bool Evaluate(ResourceFsSnapshot snapshot, string? parametersJson)
    {
        TParameters? typed = null;
        if (!string.IsNullOrEmpty(parametersJson))
        {
            try
            {
                typed = JsonSerializer.Deserialize<TParameters>(parametersJson, SerializerOptions);
            }
            catch (JsonException)
            {
                typed = null;
            }
        }

        return EvaluateInternal(snapshot, typed);
    }

    protected abstract bool EvaluateInternal(ResourceFsSnapshot snapshot, TParameters? parameters);

    protected static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>Parameter-less file predicate.</summary>
public abstract class FilePredicate : IFilePredicate
{
    public abstract string Id { get; }
    public Type ParametersType => typeof(object);

    public virtual string DisplayNameKey => $"predicate.{Id}.name";

    public bool Evaluate(ResourceFsSnapshot snapshot, string? parametersJson) => EvaluateInternal(snapshot);

    protected abstract bool EvaluateInternal(ResourceFsSnapshot snapshot);
}
