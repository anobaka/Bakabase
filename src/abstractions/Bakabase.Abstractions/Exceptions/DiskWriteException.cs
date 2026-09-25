namespace Bakabase.Abstractions.Exceptions;

/// <summary>
/// Writing a local file failed: the disk is full, the folder is read-only, the file is locked… The user has to
/// fix it; repeating the same work (a download, a merge) fails the same way and only wastes bandwidth, so this is
/// deliberately not a transient error (it derives from <see cref="IOException"/>, which
/// <c>TransientNetworkError</c> never counts on its own).
/// </summary>
public sealed class DiskWriteException : IOException, IUserActionableException
{
    public DiskWriteException(string path, bool isDiskFull, Exception? inner = null)
        : base(BuildMessage(path, isDiskFull, inner), inner)
    {
        Path = path;
        IsDiskFull = isDiskFull;
    }

    /// <summary>The file (or folder) that could not be written.</summary>
    public string Path { get; }

    /// <summary>Whether the cause is a full disk (as opposed to permissions, a lock…).</summary>
    public bool IsDiskFull { get; }

    /// <summary>
    /// A <see cref="DiskWriteException"/> for a failure while writing <paramref name="path"/>: the failure itself
    /// when it already is one, otherwise a new one whose <see cref="IsDiskFull"/> comes from
    /// <see cref="IsDiskFullError"/>.
    /// </summary>
    public static DiskWriteException From(string path, Exception writeError) =>
        writeError as DiskWriteException ?? new DiskWriteException(path, IsDiskFullError(writeError), writeError);

    /// <summary>Runs a local write; a file-system failure (<see cref="IOException"/>,
    /// <see cref="UnauthorizedAccessException"/>) becomes <see cref="From"/>(<paramref name="path"/>, …).</summary>
    public static void Guard(string path, Action write)
    {
        try
        {
            write();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw From(path, e);
        }
    }

    /// <inheritdoc cref="Guard"/>
    public static async Task GuardAsync(string path, Func<Task> write)
    {
        try
        {
            await write();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw From(path, e);
        }
    }

    /// <summary>
    /// Whether <paramref name="exception"/> or one of its inner exceptions says the disk is full: Windows
    /// <c>ERROR_DISK_FULL</c> (0x80070070) / <c>ERROR_HANDLE_DISK_FULL</c> (0x80070027), POSIX <c>ENOSPC</c>
    /// (28, which .NET reports as the HResult on Unix), or the corresponding system messages.
    /// </summary>
    public static bool IsDiskFullError(Exception? exception)
    {
        for (var e = exception; e != null; e = e.InnerException)
        {
            if (e is DiskWriteException {IsDiskFull: true})
            {
                return true;
            }

            if (e is IOException io && (io.HResult is unchecked((int) 0x80070070) or unchecked((int) 0x80070027) or 28 ||
                                        IsDiskFullMessage(io.Message)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a tool's or the system's error text says the disk is full.</summary>
    public static bool IsDiskFullMessage(string? text) =>
        !string.IsNullOrEmpty(text) &&
        (text.Contains("No space left", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("not enough space", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("disk is full", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("disk full", StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(string path, bool isDiskFull, Exception? inner) =>
        isDiskFull
            ? $"Not enough free disk space to write '{path}'."
            : $"Could not write '{path}'{(inner == null ? "" : $": {inner.Message}")}";
}
