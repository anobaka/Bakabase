namespace Bakabase.Abstractions.Components.FileSystem;

public interface IFileManager
{
    string BaseDir { get; }
    string BuildAbsolutePath(params object[] segmentsAfterBaseDir);

    /// <summary>
    /// Writes <paramref name="data"/> to <paramref name="path"/>, creating parent directories as needed.
    /// <paramref name="path"/> may be either a relative segment under <see cref="BaseDir"/>
    /// or an absolute path obtained from one of the well-known helpers below.
    /// </summary>
    Task<string> Save(string path, byte[] data, CancellationToken ct);

    // Well-known data layout under BaseDir. New writes MUST go through these
    // helpers; existing absolute paths persisted in the DB still resolve under
    // their original locations.

    /// <summary>{BaseDir}/covers/local/{resourceId} — without extension; caller decides format.</summary>
    string GetLocalCoverPathWithoutExtension(int resourceId);

    /// <summary>{BaseDir}/covers/source/{source}/{resourceId} — directory for third-party cover downloads.</summary>
    string GetSourceCoverDir(string source, int resourceId);

    /// <summary>{BaseDir}/covers/manual/{resourceId}-{index}.jpg — user-saved covers.</summary>
    string GetManualCoverPath(int resourceId, int index);

    /// <summary>{BaseDir}/enhancers/{enhancerId}/{fileName}</summary>
    string GetEnhancerFilePath(string enhancerId, string fileName);

    /// <summary>{BaseDir}/enhancers/{enhancerId}/{resourceIdPart}/{fileName}</summary>
    string GetEnhancerResourceFilePath(string enhancerId, string resourceIdPart, string fileName);

    /// <summary>{BaseDir}/attachments/{fileName}</summary>
    string GetAttachmentPath(string fileName);

    /// <summary>{BaseDir}/aigc/g{generatorId} — root dir for one generator's outputs.</summary>
    string GetAigcGeneratorDir(int generatorId);

    /// <summary>{BaseDir}/aigc/g{generatorId}/r{runId} — dir for one run's outputs.</summary>
    string GetAigcRunDir(int generatorId, int runId);

    /// <summary>Relative path under BaseDir for an AIGC artifact, suitable for DB storage.</summary>
    string GetAigcArtifactRelativePath(int generatorId, int runId, string fileName);
}
