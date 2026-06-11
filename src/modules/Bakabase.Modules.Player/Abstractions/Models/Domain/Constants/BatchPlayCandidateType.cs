namespace Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;

public enum BatchPlayCandidateType
{
    /// <summary>
    /// A player configured in the resource profiles of the selected resources.
    /// </summary>
    ProfilePlayer = 1,

    /// <summary>
    /// A player from the built-in catalog discovered on this machine.
    /// </summary>
    KnownPlayer = 2,
}
