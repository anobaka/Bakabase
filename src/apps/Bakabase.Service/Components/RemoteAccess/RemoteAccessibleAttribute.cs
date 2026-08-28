using System;

namespace Bakabase.Service.Components.RemoteAccess
{
    /// <summary>
    /// Marks a controller or action as reachable from a device other than the host.
    /// <para>
    /// The gate is default-deny: an endpoint without this attribute is refused for
    /// remote callers, so a newly added endpoint is closed until someone decides it
    /// is safe to open. Put it on a controller to open all of its actions, and use
    /// <c>[RemoteAccessible(false)]</c> on the individual actions that must stay on
    /// the host.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class RemoteAccessibleAttribute : Attribute
    {
        public RemoteAccessibleAttribute(bool allowed = true)
        {
            Allowed = allowed;
        }

        public bool Allowed { get; }

        /// <summary>
        /// Names of action parameters carrying a filesystem path. Each is checked
        /// against <see cref="Bakabase.Modules.RemoteAccess.Abstractions.Components.IMediaPathGuard"/>
        /// before the action runs, so a remote caller can only read what sits under a
        /// media library or Bakabase's own cache.
        /// </summary>
        public string[] PathParameters { get; set; } = [];

        /// <summary>
        /// Reachable before a device has paired. Only the pairing handshake itself
        /// should set this.
        /// </summary>
        public bool AllowAnonymous { get; set; }
    }
}
