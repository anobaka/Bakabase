namespace Bakabase.Abstractions.Exceptions;

/// <summary>A user-selected path is outside the allowed storage boundary.</summary>
public sealed class UserStoragePathException(string message, bool mountUnavailable = false) : IOException(message), IUserActionableException
{
    public bool MountUnavailable { get; } = mountUnavailable;
}
