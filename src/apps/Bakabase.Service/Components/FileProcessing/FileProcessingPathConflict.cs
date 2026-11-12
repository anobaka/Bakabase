using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Logical names only; a conflict never grants permission to overwrite or discard a file.</summary>
public sealed record FileProcessingPathProjectionEntry(string SourcePath, string TargetPath, bool IsDirectory = false);

public sealed record FileProcessingPathConflict(string Kind, string StepId, string TargetPath,
    IReadOnlyList<FileProcessingPathProjectionEntry> Entries, string Message);

internal static class FileProcessingPathProjection
{
    public const string FailureCode = "ProcessingPathConflict";

    // Keep native naming for ordinary operations. Explicit directory-layout operations already
    // promise portable paths and therefore also reject case and Unicode aliases on Linux.
    public static FileProcessingPathConflict? Inspect(string stepId,
        IEnumerable<FileProcessingPathProjectionEntry> entries, bool portable = false)
    {
        var comparer = portable ? StringComparer.OrdinalIgnoreCase : FileProcessingFiles.PathComparer;
        string Key(string path) => portable ? path.Normalize(NormalizationForm.FormC) : path;
        var nodes = new Dictionary<string, (string Path, bool File, FileProcessingPathProjectionEntry Entry)>(comparer);
        foreach (var entry in entries)
        {
            var path = entry.TargetPath.Replace('\\', '/');
            if (path.Length == 0 && entry.IsDirectory) continue;
            var parts = path.Split('/');
            for (var index = 1; index <= parts.Length; index++)
            {
                var candidate = string.Join('/', parts.Take(index));
                var file = !entry.IsDirectory && index == parts.Length;
                var key = Key(candidate);
                if (nodes.TryGetValue(key, out var previous))
                {
                    if (file || previous.File || previous.Path != candidate)
                    {
                        var kind = previous.File != file ? "FileDirectoryConflict" :
                            previous.Path != candidate ? "PathAliasCollision" : "PlannedTargetCollision";
                        return new(kind, stepId, candidate, [previous.Entry, entry], kind switch
                        {
                            "FileDirectoryConflict" => "A projected file conflicts with a required directory. Revise the processing plan.",
                            "PathAliasCollision" => "Projected paths refer to the same name under the selected naming rules. Revise the processing plan.",
                            _ => "Multiple input files would have the same output path. Revise the processing plan."
                        });
                    }
                }
                else nodes.Add(key, (candidate, file, entry));
            }
        }
        return null;
    }
}
