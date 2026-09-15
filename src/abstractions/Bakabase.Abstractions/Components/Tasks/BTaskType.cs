namespace Bakabase.Abstractions.Components.Tasks;

public enum BTaskType
{
    Decompress = 1,
    MoveFiles = 2,
    MoveResources = 3,
    CopyFiles = 4,
    Download = 5,

    /// <summary>Getting a resource the user does not have yet.</summary>
    Acquisition = 6,

    Any = 1000
}