using System.Text.Json;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Modules.AI.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Services;

public class AiFileProcessorService(
    ILlmService llmService,
    IBOptionsManager<AppOptions> appOptionsManager,
    ILogger<AiFileProcessorService> logger
) : IAiFileProcessorService
{
    private string LanguageInstruction
    {
        get
        {
            var lang = appOptionsManager.Value?.Language;
            return string.IsNullOrEmpty(lang) ? "" : $"\nIMPORTANT: You must respond in {lang}.";
        }
    }

    private static string BuildWorkingDirectoryHint(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return "";
        return $"\nThe user is currently working in this directory: {workingDirectory}";
    }

    private static string BuildReferencePathsHint(IReadOnlyList<string>? referencePaths)
    {
        if (referencePaths == null || referencePaths.Count == 0)
            return "";
        return $"""

            IMPORTANT: The following paths are considered well-organized reference examples by the user. Do NOT suggest any changes to these paths. Instead, use them as the standard to guide your suggestions for other paths:
            {string.Join("\n", referencePaths)}
            """;
    }

    public async Task<FileStructureAnalysisResult> AnalyzeFileStructureAsync(
        string directoryPath, IReadOnlyList<string>? referencePaths = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(directoryPath))
            return new FileStructureAnalysisResult
            {
                DirectoryPath = directoryPath,
                Issues = [$"Directory not found: {directoryPath}"]
            };

        var entries = GatherDirectoryInfo(directoryPath, maxDepth: 3);
        var referenceHint = BuildReferencePathsHint(referencePaths);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, $$"""
                You are a file organization expert. Analyze the given directory structure and identify issues.
                Respond with a JSON object:
                {
                  "issues": ["issue1", "issue2"],
                  "suggestions": ["suggestion1", "suggestion2"],
                  "summary": "Brief overall assessment",
                  "operations": [
                    {"type": "move", "sourcePath": "current/full/path", "destinationPath": "better/full/path", "reason": "why"}
                  ]
                }
                For operations:
                - "type" must be one of: "rename", "move", "createDirectory"
                - "sourcePath" and "destinationPath" must be full absolute paths
                - Only include operations that would meaningfully improve the structure
                - Ensure parent directory operations come before child operations
                {{referenceHint}}
                {{LanguageInstruction}}
                """),
            new(ChatRole.User, $"Analyze this directory structure:\n{entries}")
        };

        var response = await llmService.CompleteForFeatureAsync(AiFeature.FileProcessor, messages, ct: ct);
        var rawText = response.Text?.Trim() ?? "";
        var json = ExtractJson(rawText);

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new FileStructureAnalysisResult
            {
                DirectoryPath = directoryPath,
                TotalFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories).Length,
                TotalDirectories = Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories).Length,
                Issues = GetStringArray(root, "issues"),
                Suggestions = GetStringArray(root, "suggestions"),
                Summary = root.TryGetProperty("summary", out var s) ? s.GetString() : null,
                Operations = ParseOperations(root, directoryPath)
            };
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse AI response for file structure analysis");
            return new FileStructureAnalysisResult
            {
                DirectoryPath = directoryPath,
                Summary = null,
                RawText = rawText
            };
        }
    }

    public async Task<NamingConventionAnalysisResult> AnalyzeNamingConventionAsync(
        IReadOnlyList<string> filePaths, string? workingDirectory = null, IReadOnlyList<string>? referencePaths = null, CancellationToken ct = default)
    {
        var fileInfos = filePaths.Select(p => new { Path = p, Name = Path.GetFileName(p) }).ToList();
        var referenceHint = BuildReferencePathsHint(referencePaths);
        var workingDirHint = BuildWorkingDirectoryHint(workingDirectory);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, $$"""
                You are a file naming convention expert. Analyze the given file names and detect the naming pattern.{{workingDirHint}}
                Respond with a JSON object:
                {
                  "detectedPattern": "Pattern description",
                  "inconsistencies": ["file1 doesn't match because..."],
                  "suggestions": ["suggestion1"],
                  "summary": "Brief assessment",
                  "operations": [
                    {"type": "rename", "sourcePath": "original/full/path/file.txt", "destinationPath": "original/full/path/corrected_file.txt", "reason": "why"}
                  ]
                }
                For operations:
                - Use "rename" type for file name corrections
                - "sourcePath" and "destinationPath" must be full absolute paths
                - Only include files that need renaming to match the detected pattern
                {{referenceHint}}
                {{LanguageInstruction}}
                """),
            new(ChatRole.User,
                $"Analyze naming conventions for these files:\n{string.Join("\n", fileInfos.Select(f => f.Path).Take(100))}")
        };

        var response = await llmService.CompleteForFeatureAsync(AiFeature.FileProcessor, messages, ct: ct);
        var rawText = response.Text?.Trim() ?? "";
        var json = ExtractJson(rawText);

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new NamingConventionAnalysisResult
            {
                DetectedPattern = root.TryGetProperty("detectedPattern", out var p) ? p.GetString() : null,
                Inconsistencies = GetStringArray(root, "inconsistencies"),
                Suggestions = GetStringArray(root, "suggestions"),
                Summary = root.TryGetProperty("summary", out var s) ? s.GetString() : null,
                Operations = ParseOperations(root, null)
            };
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse AI response for naming convention analysis");
            return new NamingConventionAnalysisResult { RawText = rawText };
        }
    }

    public async Task<FileNameCorrectionResult> SuggestFileNameCorrectionsAsync(
        IReadOnlyList<string> filePaths, string? workingDirectory = null, string? targetConvention = null, IReadOnlyList<string>? referencePaths = null, CancellationToken ct = default)
    {
        var fileNames = filePaths.Select(p => new { Path = p, Name = Path.GetFileName(p) }).ToList();
        var conventionHint = targetConvention != null
            ? $"\nTarget naming convention: {targetConvention}"
            : "";
        var referenceHint = BuildReferencePathsHint(referencePaths);
        var workingDirHint = BuildWorkingDirectoryHint(workingDirectory);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, $$"""
                                   You are a file naming expert. Suggest corrections for inconsistent file names.{{workingDirHint}}{{conventionHint}}
                                   Respond with a JSON object:
                                   {
                                     "appliedConvention": "Convention used",
                                     "corrections": [
                                       {"originalName": "old.txt", "suggestedName": "new.txt", "reason": "why"}
                                     ]
                                   }
                                   Only include files that need corrections. Return empty corrections array if all names are consistent.
                                   {{referenceHint}}
                                   {{LanguageInstruction}}
                                   """),
            new(ChatRole.User,
                $"Suggest corrections for these file names:\n{string.Join("\n", fileNames.Select(f => $"{f.Path}").Take(100))}")
        };

        var response = await llmService.CompleteForFeatureAsync(AiFeature.FileProcessor, messages, ct: ct);
        var rawText = response.Text?.Trim() ?? "";
        var json = ExtractJson(rawText);

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var corrections = new List<FileNameCorrection>();
            var operations = new List<FileOperation>();

            if (root.TryGetProperty("corrections", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var nameToPath = fileNames.ToDictionary(f => f.Name ?? "", f => f.Path);
                var order = 0;
                foreach (var item in arr.EnumerateArray())
                {
                    var originalName = item.TryGetProperty("originalName", out var on) ? on.GetString() : null;
                    var suggestedName = item.TryGetProperty("suggestedName", out var sn) ? sn.GetString() : null;
                    var reason = item.TryGetProperty("reason", out var r) ? r.GetString() : "";

                    if (originalName != null && suggestedName != null &&
                        !string.Equals(originalName, suggestedName, StringComparison.Ordinal) &&
                        nameToPath.TryGetValue(originalName, out var originalPath))
                    {
                        corrections.Add(new FileNameCorrection
                        {
                            OriginalPath = originalPath,
                            SuggestedName = suggestedName,
                            Reason = reason ?? ""
                        });

                        var dir = Path.GetDirectoryName(originalPath) ?? "";
                        operations.Add(new FileOperation
                        {
                            Type = FileOperationType.Rename,
                            SourcePath = originalPath,
                            DestinationPath = Path.Combine(dir, suggestedName),
                            Reason = reason,
                            Order = order++
                        });
                    }
                }
            }

            return new FileNameCorrectionResult
            {
                Corrections = corrections,
                AppliedConvention = root.TryGetProperty("appliedConvention", out var ac) ? ac.GetString() : null,
                Operations = operations
            };
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse AI response for file name corrections");
            return new FileNameCorrectionResult { RawText = rawText };
        }
    }

    public async Task<PathSimilarityGroupResult> GroupByPathSimilarityAsync(
        IReadOnlyList<string> filePaths, string? workingDirectory = null, string? customGroupingLogic = null, CancellationToken ct = default)
    {
        var hasCustomLogic = !string.IsNullOrWhiteSpace(customGroupingLogic);
        var groupingInstruction = hasCustomLogic
            ? $"Group the given files strictly according to the following user-defined criteria: {customGroupingLogic}\nDo NOT fall back to other grouping strategies (such as file type or directory). Only use the criteria specified above."
            : "Group the given files by similarity.";

        var workingDirHint = BuildWorkingDirectoryHint(workingDirectory);
        var fileList = filePaths.Take(200)
            .Select(p => $"- Filename: {Path.GetFileName(p)} (without extension: {Path.GetFileNameWithoutExtension(p)}) | Full path: {p}")
            .ToList();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, $$"""
                You are a file organization expert. {{groupingInstruction}}{{workingDirHint}}
                IMPORTANT: "Filename" refers to the base name of the file (the last segment of the path), NOT the directory name. For example, in "/Users/test/abc.jpg", the filename is "abc.jpg" and the filename without extension is "abc".
                The "groupName" should be a concise label suitable for use as a folder name.
                The "targetDirectory" should be the common parent directory of the group's files joined with the groupName.
                Respond with a JSON object:
                {
                  "groups": [
                    {
                      "groupName": "Group name",
                      "paths": ["full path 1", "full path 2"],
                      "reason": "why grouped",
                      "targetDirectory": "/common/parent/dir/groupName"
                    }
                  ]
                }
                {{LanguageInstruction}}
                """),
            new(ChatRole.User,
                $"Group these files:\n{string.Join("\n", fileList)}")
        };

        var response = await llmService.CompleteForFeatureAsync(AiFeature.FileProcessor, messages, ct: ct);
        var rawText = response.Text?.Trim() ?? "";
        var json = ExtractJson(rawText);

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var groups = new List<PathSimilarityGroup>();
            var operations = new List<FileOperation>();
            var order = 0;

            if (root.TryGetProperty("groups", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var groupName = item.TryGetProperty("groupName", out var gn)
                        ? gn.GetString() ?? ""
                        : "";
                    var paths = GetStringArray(item, "paths");
                    var reason = item.TryGetProperty("reason", out var r) ? r.GetString() : null;
                    var targetDir = item.TryGetProperty("targetDirectory", out var td) ? td.GetString() : null;

                    groups.Add(new PathSimilarityGroup
                    {
                        GroupName = groupName,
                        Paths = paths,
                        Reason = reason
                    });

                    if (!string.IsNullOrEmpty(targetDir))
                    {
                        // Create directory operation first
                        var createDirOrder = order++;
                        operations.Add(new FileOperation
                        {
                            Type = FileOperationType.CreateDirectory,
                            SourcePath = targetDir,
                            DestinationPath = targetDir,
                            Reason = $"Create directory for group: {groupName}",
                            Order = createDirOrder
                        });

                        // Then move each file into the target directory
                        foreach (var path in paths)
                        {
                            var fileName = Path.GetFileName(path);
                            operations.Add(new FileOperation
                            {
                                Type = FileOperationType.Move,
                                SourcePath = path,
                                DestinationPath = Path.Combine(targetDir, fileName),
                                Reason = reason,
                                Order = order++,
                                DependsOnOrder = createDirOrder
                            });
                        }
                    }
                }
            }

            return new PathSimilarityGroupResult { Groups = groups, Operations = operations };
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse AI response for path similarity grouping");
            return new PathSimilarityGroupResult { RawText = rawText };
        }
    }

    public async Task<DirectoryStructureCorrectionResult> SuggestDirectoryCorrectionsAsync(
        string directoryPath, IReadOnlyList<string>? referencePaths = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(directoryPath))
            return new DirectoryStructureCorrectionResult
            {
                DirectoryPath = directoryPath,
                Summary = $"Directory not found: {directoryPath}"
            };

        var entries = GatherDirectoryInfo(directoryPath, maxDepth: 3);
        var referenceHint = BuildReferencePathsHint(referencePaths);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, $$"""
                You are a directory structure expert. Suggest improvements for the directory organization.
                Respond with a JSON object:
                {
                  "corrections": [
                    {"originalPath": "current/full/path", "suggestedPath": "better/full/path", "reason": "why"}
                  ],
                  "summary": "Brief assessment"
                }
                Only suggest changes that would meaningfully improve organization.
                "originalPath" and "suggestedPath" must be full absolute paths.
                Ensure parent directory corrections come before child corrections.
                {{referenceHint}}
                {{LanguageInstruction}}
                """),
            new(ChatRole.User, $"Suggest directory structure improvements:\n{entries}")
        };

        var response = await llmService.CompleteForFeatureAsync(AiFeature.FileProcessor, messages, ct: ct);
        var rawText = response.Text?.Trim() ?? "";
        var json = ExtractJson(rawText);

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var corrections = new List<DirectoryCorrection>();
            var operations = new List<FileOperation>();
            var order = 0;

            if (root.TryGetProperty("corrections", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var originalPath = item.TryGetProperty("originalPath", out var op)
                        ? op.GetString() ?? ""
                        : "";
                    var suggestedPath = item.TryGetProperty("suggestedPath", out var sp)
                        ? sp.GetString() ?? ""
                        : "";
                    var reason = item.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

                    corrections.Add(new DirectoryCorrection
                    {
                        OriginalPath = originalPath,
                        SuggestedPath = suggestedPath,
                        Reason = reason
                    });

                    if (!string.IsNullOrEmpty(originalPath) && !string.IsNullOrEmpty(suggestedPath))
                    {
                        operations.Add(new FileOperation
                        {
                            Type = FileOperationType.Move,
                            SourcePath = originalPath,
                            DestinationPath = suggestedPath,
                            Reason = reason,
                            Order = order++
                        });
                    }
                }
            }

            InferMoveDependencies(operations);
            return new DirectoryStructureCorrectionResult
            {
                DirectoryPath = directoryPath,
                Corrections = corrections,
                Summary = root.TryGetProperty("summary", out var s) ? s.GetString() : null,
                Operations = operations
            };
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse AI response for directory corrections");
            return new DirectoryStructureCorrectionResult
            {
                DirectoryPath = directoryPath,
                RawText = rawText
            };
        }
    }

    public async Task<ApplyOperationsResult> ApplyOperationsAsync(
        IReadOnlyList<FileOperation> operations, CancellationToken ct = default)
    {
        var sorted = operations.OrderBy(o => o.Order).ToList();
        var errors = new List<OperationError>();
        var successCount = 0;

        for (var i = 0; i < sorted.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var op = sorted[i];

            try
            {
                switch (op.Type)
                {
                    case FileOperationType.CreateDirectory:
                        Directory.CreateDirectory(op.DestinationPath);
                        break;

                    case FileOperationType.Rename:
                    {
                        if (File.Exists(op.SourcePath))
                        {
                            var destDir = Path.GetDirectoryName(op.DestinationPath);
                            if (!string.IsNullOrEmpty(destDir))
                                Directory.CreateDirectory(destDir);
                            File.Move(op.SourcePath, op.DestinationPath);
                        }
                        else if (Directory.Exists(op.SourcePath))
                        {
                            Directory.Move(op.SourcePath, op.DestinationPath);
                        }
                        else
                        {
                            throw new FileNotFoundException($"Source not found: {op.SourcePath}");
                        }

                        break;
                    }

                    case FileOperationType.Move:
                    {
                        var destDir = Path.GetDirectoryName(op.DestinationPath);
                        if (!string.IsNullOrEmpty(destDir))
                            Directory.CreateDirectory(destDir);

                        if (File.Exists(op.SourcePath))
                        {
                            File.Move(op.SourcePath, op.DestinationPath);
                        }
                        else if (Directory.Exists(op.SourcePath))
                        {
                            Directory.Move(op.SourcePath, op.DestinationPath);
                        }
                        else
                        {
                            throw new FileNotFoundException($"Source not found: {op.SourcePath}");
                        }

                        break;
                    }

                    default:
                        throw new NotSupportedException($"Unsupported operation type: {op.Type}");
                }

                successCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to apply file operation: {Type} {Source} -> {Dest}",
                    op.Type, op.SourcePath, op.DestinationPath);
                errors.Add(new OperationError
                {
                    OperationIndex = i,
                    Operation = op,
                    ErrorMessage = ex.Message
                });
            }
        }

        return new ApplyOperationsResult
        {
            TotalOperations = sorted.Count,
            SuccessCount = successCount,
            FailureCount = errors.Count,
            Errors = errors
        };
    }

    private static List<FileOperation> ParseOperations(JsonElement root, string? basePath)
    {
        var operations = new List<FileOperation>();
        if (!root.TryGetProperty("operations", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return operations;

        var order = 0;
        foreach (var item in arr.EnumerateArray())
        {
            var typeStr = item.TryGetProperty("type", out var t) ? t.GetString()?.ToLowerInvariant() : null;
            var source = item.TryGetProperty("sourcePath", out var sp) ? sp.GetString() ?? "" : "";
            var dest = item.TryGetProperty("destinationPath", out var dp) ? dp.GetString() ?? "" : "";
            var reason = item.TryGetProperty("reason", out var r) ? r.GetString() : null;

            if (string.IsNullOrEmpty(typeStr) || string.IsNullOrEmpty(source))
                continue;

            var opType = typeStr switch
            {
                "rename" => FileOperationType.Rename,
                "move" => FileOperationType.Move,
                "createdirectory" or "create_directory" or "mkdir" => FileOperationType.CreateDirectory,
                _ => (FileOperationType?)null
            };

            if (opType == null) continue;

            operations.Add(new FileOperation
            {
                Type = opType.Value,
                SourcePath = source,
                DestinationPath = dest,
                Reason = reason,
                Order = order++
            });
        }

        InferDependencies(operations);
        return operations;
    }

    /// <summary>
    /// Infer parent-child dependencies between operations based on path relationships.
    /// A Move/Rename operation depends on a prior CreateDirectory if its destination is under that directory.
    /// A CreateDirectory depends on a prior CreateDirectory if it is a subdirectory.
    /// </summary>
    private static void InferDependencies(List<FileOperation> operations)
    {
        // Build a lookup of CreateDirectory operations by their destination path
        var createDirOps = operations
            .Where(op => op.Type == FileOperationType.CreateDirectory)
            .OrderBy(op => op.Order)
            .ToList();

        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            if (op.DependsOnOrder != null) continue;

            // Find the best matching parent CreateDirectory:
            // the one whose destination path is the longest prefix of this operation's
            // destination directory, and comes before this operation in order.
            var destDir = op.Type == FileOperationType.CreateDirectory
                ? Path.GetDirectoryName(op.DestinationPath)
                : Path.GetDirectoryName(op.DestinationPath);

            if (string.IsNullOrEmpty(destDir)) continue;

            FileOperation? bestParent = null;
            var bestPathLen = 0;

            foreach (var dirOp in createDirOps)
            {
                if (dirOp.Order >= op.Order) continue;
                var dirPath = dirOp.DestinationPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // Check if this CreateDirectory's path is a prefix of the current op's destination directory
                if (destDir.Equals(dirPath, StringComparison.OrdinalIgnoreCase) ||
                    destDir.StartsWith(dirPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    destDir.StartsWith(dirPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    if (dirPath.Length > bestPathLen)
                    {
                        bestPathLen = dirPath.Length;
                        bestParent = dirOp;
                    }
                }
            }

            if (bestParent != null)
            {
                operations[i] = op with { DependsOnOrder = bestParent.Order };
            }
        }
    }

    /// <summary>
    /// Infer dependencies between Move operations based on source path nesting.
    /// If operation A moves /parent and operation B moves /parent/child,
    /// then B depends on A (parent must be moved first).
    /// </summary>
    private static void InferMoveDependencies(List<FileOperation> operations)
    {
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            if (op.DependsOnOrder != null) continue;

            var sourcePath = op.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            FileOperation? bestParent = null;
            var bestPathLen = 0;

            for (var j = 0; j < operations.Count; j++)
            {
                if (j == i) continue;
                var other = operations[j];
                if (other.Order >= op.Order) continue;

                var otherSource = other.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // Check if this operation's source is nested under the other's source
                if (sourcePath.StartsWith(otherSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    sourcePath.StartsWith(otherSource + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    if (otherSource.Length > bestPathLen)
                    {
                        bestPathLen = otherSource.Length;
                        bestParent = other;
                    }
                }
            }

            if (bestParent != null)
            {
                operations[i] = op with { DependsOnOrder = bestParent.Order };
            }
        }
    }

    private static string GatherDirectoryInfo(string directoryPath, int maxDepth)
    {
        var lines = new List<string>();
        GatherRecursive(directoryPath, 0, maxDepth, lines, "");
        return string.Join("\n", lines.Take(500));
    }

    private static void GatherRecursive(string path, int depth, int maxDepth, List<string> lines, string indent)
    {
        if (depth > maxDepth || lines.Count > 500) return;

        var dirName = Path.GetFileName(path);
        lines.Add($"{indent}{dirName}/");

        try
        {
            foreach (var file in Directory.GetFiles(path).Take(50))
            {
                lines.Add($"{indent}  {Path.GetFileName(file)}");
            }

            foreach (var dir in Directory.GetDirectories(path).Take(30))
            {
                GatherRecursive(dir, depth + 1, maxDepth, lines, indent + "  ");
            }
        }
        catch (UnauthorizedAccessException)
        {
            lines.Add($"{indent}  [access denied]");
        }
    }

    private static string ExtractJson(string text)
    {
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
                text = text[(firstNewline + 1)..];
            if (text.EndsWith("```"))
                text = text[..^3];
            text = text.Trim();
        }

        return text;
    }

    private static List<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            return arr.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString()!)
                .ToList();
        }

        return [];
    }
}
