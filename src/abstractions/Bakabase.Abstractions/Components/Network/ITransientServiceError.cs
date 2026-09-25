namespace Bakabase.Abstractions.Components.Network;

/// <summary>
/// Marks an exception meaning a remote service answered "not right now" in-band — for example an
/// HTTP 200 carrying a risk-control or overload code. <see cref="TransientNetworkError"/> treats it
/// like a 429/5xx, so a caller that retries transient failures waits and tries again instead of
/// failing for good.
/// </summary>
/// <remarks>
/// Opt-in: only exception types that really mean "the same request may succeed later" implement it.
/// The caller's own cancellation still wins (see <see cref="TransientNetworkError.IsTransient"/>).
/// </remarks>
public interface ITransientServiceError
{
}
