using Bakabase.Modules.AI.Services;

namespace Bakabase.Modules.AI.Models.Input;

public record FileProcessorDirectoryInputModel
{
    public required string DirectoryPath { get; init; }
    public IReadOnlyList<string>? ReferencePaths { get; init; }
}

public record FileProcessorPathsInputModel
{
    public required IReadOnlyList<string> FilePaths { get; init; }
    public string? WorkingDirectory { get; init; }
    public IReadOnlyList<string>? ReferencePaths { get; init; }
}

public record FileNameCorrectionInputModel
{
    public required IReadOnlyList<string> FilePaths { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? TargetConvention { get; init; }
    public IReadOnlyList<string>? ReferencePaths { get; init; }
}

public record PathSimilarityGroupInputModel
{
    public required IReadOnlyList<string> FilePaths { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? CustomGroupingLogic { get; init; }
}

public record ApplyFileOperationsInputModel
{
    public required IReadOnlyList<FileOperation> Operations { get; init; }
}
