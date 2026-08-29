using System;

namespace Bakabase.Service.Components.RemoteAccess
{
    /// <summary>
    /// Marks a controller or action as meaningful from a device other than the host.
    /// <para>
    /// This is not a permission: every device that can reach Bakabase is trusted.
    /// It records where an action's effect lands. Launching a player, opening a
    /// folder or deleting a file happens on the host machine, so calling it from a
    /// phone would put a window on a screen the caller cannot see and report success.
    /// </para>
    /// <para>
    /// Default-deny, so an endpoint nobody has thought about yet fails visibly
    /// ("not available from this device") rather than doing something invisible on
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
    }
}
