namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// Which flavour of Bakabase the frontend is talking to, and therefore where an action
/// that has to run on a person's own machine would actually run.
/// </summary>
/// <remarks>
/// Deliberately separate from <c>isLocal</c>, which answers a narrower question: is the
/// caller on the machine running the server. The desktop app showing a server it manages is
/// not — that server may be in another building — yet launching a player still works there,
/// because the app's relay runs it here. Collapsing the two would either hide the play
/// button on a window that can play, or send the UI looking for files on a machine that
/// does not have them.
/// </remarks>
public enum ClientMode
{
    /// <summary>The desktop app running its own server. Also what an older backend, saying nothing, means.</summary>
    AllInOne = 0,

    /// <summary>An ordinary browser pointed at a server over the network. Nothing user-side runs.</summary>
    RemoteBrowser = 1,

    /// <summary>
    /// The desktop app's relay forwarding to a server it manages elsewhere. User-side actions
    /// run here.
    /// </summary>
    PureClient = 2
}
