using System.IO;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingWorkspaceResolver
{
    internal string StageCleanupMetadataDirectory => Path.Combine(MetadataRoot, "stage-directory-cleanup");
}
