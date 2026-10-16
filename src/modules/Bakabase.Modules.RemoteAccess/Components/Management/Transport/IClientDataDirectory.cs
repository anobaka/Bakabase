namespace Bakabase.Remoting.Abstractions;

/// <summary>
/// Where the client keeps its own state.
/// </summary>
/// <remarks>
/// Split the same way the server's is, and for the same reason: <see cref="Path"/>
/// resolves without creating anything, so a read on a client that has never paired
/// leaves no empty folder behind, while <see cref="Ensure"/> is what a write goes
/// through.
/// </remarks>
public interface IClientDataDirectory
{
    /// <summary>Resolves the path. Creates nothing.</summary>
    string Path { get; }

    /// <summary>Creates the directory if needed and returns it.</summary>
    string Ensure();
}
