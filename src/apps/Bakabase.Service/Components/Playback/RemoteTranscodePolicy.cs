using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Service.Components.Playback
{
    /// <summary>
    /// Whether a caller may start a live ffmpeg transcode. Kept as a pure function
    /// for the same reason as <see cref="VideoDeliveryPlanner"/>: a wrong answer
    /// does not throw, it either burns host CPU for a stranger or blocks the
    /// owner's own playback.
    /// </summary>
    public static class RemoteTranscodePolicy
    {
        /// <summary>
        /// True when the request must be refused: a remote caller under
        /// <see cref="Bakabase.Abstractions.Models.Domain.Constants.RemoteAccessMode.Enabled"/>
        /// while the host has not allowed remote transcodes. Loopback callers and
        /// Unrestricted mode (where the remote browser belongs to the operator) are
        /// never refused; neither is a request the middleware has not classified,
        /// which can only be an in-process one.
        /// </summary>
        public static bool ShouldRefuse(RemoteAccessContext? context, bool allowLiveTranscode) =>
            context is {IsUnrestricted: false} && !allowLiveTranscode;
    }
}
