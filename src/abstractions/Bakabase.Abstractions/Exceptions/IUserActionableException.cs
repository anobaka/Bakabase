namespace Bakabase.Abstractions.Exceptions
{
    /// <summary>
    /// Marks an exception that reports a condition the *user* has to resolve — a dependency
    /// that is not installed yet, an expired third-party cookie, a path that does not exist —
    /// rather than a defect in Bakabase.
    ///
    /// These are surfaced to the user through the normal error channels (controller response,
    /// task error message), but they are deliberately dropped before reaching the error
    /// dashboard: they are not actionable for us, and in aggregate they drown out real crashes.
    /// See the <c>SetBeforeSend</c> filter in <c>BakabaseStartup</c>.
    ///
    /// Implement this on the exception type instead of adding another special case to that
    /// filter — the filter walks the whole <see cref="System.Exception.InnerException"/> chain,
    /// so a wrapped instance is dropped too.
    /// </summary>
    public interface IUserActionableException
    {
    }
}
