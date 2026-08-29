namespace Bakabase.Modules.RemoteAccess.Abstractions.Components;

/// <summary>
/// Supplies the addresses Kestrel is actually bound to (e.g.
/// <c>http://0.0.0.0:34567</c>). Implemented at the application layer, which is
/// where the host's own state lives.
/// </summary>
public interface IListeningAddressProvider
{
    IReadOnlyList<string> GetListeningAddresses();
}
