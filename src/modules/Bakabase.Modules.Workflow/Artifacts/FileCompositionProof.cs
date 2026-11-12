namespace Bakabase.Modules.Workflow.Artifacts;

/// <summary>A read-only union of exact registered files. Its common root is never an owned workspace.</summary>
public sealed record FileCompositionProof(int Version, string InputFingerprint,
    IReadOnlyList<FileDispositionProof> Parents, string ProvenanceJson);
