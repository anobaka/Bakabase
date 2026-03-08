namespace Bakabase.Modules.AI.Services;

public interface IAiFileProcessorService
{
    /// <summary>
    /// Analyze file structure under a directory and provide improvement suggestions.
    /// </summary>
    Task<FileStructureAnalysisResult> AnalyzeFileStructureAsync(
        string directoryPath,
        IReadOnlyList<string>? referencePaths = null,
        CancellationToken ct = default);

    /// <summary>
    /// Analyze file naming conventions and detect inconsistencies.
    /// </summary>
    Task<NamingConventionAnalysisResult> AnalyzeNamingConventionAsync(
        IReadOnlyList<string> filePaths,
        string? workingDirectory = null,
        IReadOnlyList<string>? referencePaths = null,
        CancellationToken ct = default);

    /// <summary>
    /// Suggest corrected file names based on detected naming conventions.
    /// </summary>
    Task<FileNameCorrectionResult> SuggestFileNameCorrectionsAsync(
        IReadOnlyList<string> filePaths,
        string? workingDirectory = null,
        string? targetConvention = null,
        IReadOnlyList<string>? referencePaths = null,
        CancellationToken ct = default);

    /// <summary>
    /// Group files by path similarity.
    /// </summary>
    Task<PathSimilarityGroupResult> GroupByPathSimilarityAsync(
        IReadOnlyList<string> filePaths,
        string? workingDirectory = null,
        string? customGroupingLogic = null,
        CancellationToken ct = default);

    /// <summary>
    /// Suggest directory structure corrections for better organization.
    /// </summary>
    Task<DirectoryStructureCorrectionResult> SuggestDirectoryCorrectionsAsync(
        string directoryPath,
        IReadOnlyList<string>? referencePaths = null,
        CancellationToken ct = default);

    /// <summary>
    /// Apply a list of file operations (rename/move) in order.
    /// </summary>
    Task<ApplyOperationsResult> ApplyOperationsAsync(
        IReadOnlyList<FileOperation> operations,
        CancellationToken ct = default);
}

/// <summary>
/// Types of file operations that can be applied.
/// </summary>
public enum FileOperationType
{
    Rename = 1,
    Move = 2,
    CreateDirectory = 3
}

/// <summary>
/// A single file operation suggested by AI.
/// </summary>
public record FileOperation
{
    public FileOperationType Type { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public string DestinationPath { get; init; } = string.Empty;
    public string? Reason { get; init; }

    /// <summary>
    /// Lower order executes first. Used to ensure parent directories are created/moved before children.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// The Order value of the operation this one depends on.
    /// Null means no dependency (root operation).
    /// Used to build a tree structure in the UI for cascading selection.
    /// </summary>
    public int? DependsOnOrder { get; init; }
}

public record FileStructureAnalysisResult
{
    public string DirectoryPath { get; init; } = string.Empty;
    public int TotalFiles { get; init; }
    public int TotalDirectories { get; init; }
    public List<string> Issues { get; init; } = [];
    public List<string> Suggestions { get; init; } = [];
    public string? Summary { get; init; }
    public List<FileOperation> Operations { get; init; } = [];

    /// <summary>
    /// Raw AI response text when structured parsing fails.
    /// </summary>
    public string? RawText { get; init; }
}

public record NamingConventionAnalysisResult
{
    public string? DetectedPattern { get; init; }
    public List<string> Inconsistencies { get; init; } = [];
    public List<string> Suggestions { get; init; } = [];
    public string? Summary { get; init; }
    public List<FileOperation> Operations { get; init; } = [];
    public string? RawText { get; init; }
}

public record FileNameCorrectionResult
{
    public List<FileNameCorrection> Corrections { get; init; } = [];
    public string? AppliedConvention { get; init; }
    public List<FileOperation> Operations { get; init; } = [];
    public string? RawText { get; init; }
}

public record FileNameCorrection
{
    public string OriginalPath { get; init; } = string.Empty;
    public string SuggestedName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public record PathSimilarityGroupResult
{
    public List<PathSimilarityGroup> Groups { get; init; } = [];
    public List<FileOperation> Operations { get; init; } = [];
    public string? RawText { get; init; }
}

public record PathSimilarityGroup
{
    public string GroupName { get; init; } = string.Empty;
    public List<string> Paths { get; init; } = [];
    public string? Reason { get; init; }
}

public record DirectoryStructureCorrectionResult
{
    public string DirectoryPath { get; init; } = string.Empty;
    public List<DirectoryCorrection> Corrections { get; init; } = [];
    public string? Summary { get; init; }
    public List<FileOperation> Operations { get; init; } = [];
    public string? RawText { get; init; }
}

public record DirectoryCorrection
{
    public string OriginalPath { get; init; } = string.Empty;
    public string SuggestedPath { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public record ApplyOperationsResult
{
    public int TotalOperations { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public List<OperationError> Errors { get; init; } = [];
}

public record OperationError
{
    public int OperationIndex { get; init; }
    public FileOperation Operation { get; init; } = null!;
    public string ErrorMessage { get; init; } = string.Empty;
}
