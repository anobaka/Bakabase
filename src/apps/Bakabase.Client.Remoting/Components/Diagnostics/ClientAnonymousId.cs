using Bakabase.Remoting.Abstractions;

namespace Bakabase.Client.Remoting.Components.Diagnostics;

/// <summary>
/// The client's own anonymous install id.
/// </summary>
/// <remarks>
/// <para>
/// A UUID generated on first launch and kept as one line under the client's data
/// directory. Carries nothing about the machine or the person — it is not derived from
/// any hardware identifier — and exists only so several reports from one install can be
/// recognised as one install.
/// </para>
/// <para>
/// Deliberately not the server's. The all-in-one keeps the same kind of file under its
/// own application data directory, and the two flavours never share one: somebody running
/// both is running two programs, updated separately and often on different versions, and
/// merging them into one identity would make each one's errors look like the other's.
/// </para>
/// </remarks>
public static class ClientAnonymousId
{
    public const string FileName = "anonymous-id";

    /// <summary>
    /// Reads this install's id, creating one if there is none.
    /// </summary>
    /// <remarks>
    /// Not cached: it is read once, at startup, so a cache would only be somewhere for a
    /// stale value to live.
    /// </remarks>
    public static string GetOrCreate(IClientDataDirectory directory)
    {
        var path = Path.Combine(directory.Path, FileName);

        if (File.Exists(path))
        {
            try
            {
                var existing = File.ReadAllText(path).Trim();

                if (Guid.TryParse(existing, out _))
                {
                    return existing;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Falls through to a fresh one. An install that cannot read its own id is
                // not a reason to start without reporting.
            }
        }

        var id = Guid.NewGuid().ToString("D");

        try
        {
            File.WriteAllText(Path.Combine(directory.Ensure(), FileName), id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Used for this run only; the next launch tries again. An id that changes is
            // worse than one that does not, and far better than no reporting at all.
        }

        return id;
    }
}
