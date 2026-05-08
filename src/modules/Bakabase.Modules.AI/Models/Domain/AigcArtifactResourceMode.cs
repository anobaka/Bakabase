namespace Bakabase.Modules.AI.Models.Domain;

/// <summary>
/// Determines whether each generated artifact becomes its own Resource,
/// or all artifacts of a single run are bundled into one Resource (folder).
/// </summary>
public enum AigcArtifactResourceMode
{
    PerArtifact = 1,
    PerRun = 2
}
