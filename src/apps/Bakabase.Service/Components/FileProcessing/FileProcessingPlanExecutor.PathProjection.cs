using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingPlanExecutor
{
    private sealed class PathConflictException(FileProcessingPathConflict conflict) : Exception(conflict.Message)
    {
        public FileProcessingPathConflict Conflict { get; } = conflict;
    }

    private static FileProcessingPathProjectionEntry PathEntry(Artifact item) => new(item.RelativePath, item.RelativePath);
    private static FileProcessingPathProjectionEntry PathEntry(DirectoryArtifact item) => new(item.RelativePath, item.RelativePath, true);

    private static void CheckProjection(string stepId, IEnumerable<FileProcessingPathProjectionEntry> entries, bool portable = false)
    {
        if (FileProcessingPathProjection.Inspect(stepId, entries, portable) is { } conflict)
            throw new PathConflictException(conflict);
    }

    private static void CheckUntrackedDestination(string stepId, string destination, string checkpoint)
    {
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            var path = Path.GetRelativePath(checkpoint, destination).Replace('\\', '/');
            throw new PathConflictException(new("ExistingTargetConflict", stepId, path,
                [new(path, path, Directory.Exists(destination))],
                "An output path already exists without this operation's receipt. Its contents were retained."));
        }
    }

    private static List<(Artifact Source, Artifact Output)> ProjectRename(FileProcessingStep step,
        IReadOnlyList<Artifact> supplied, IReadOnlyList<Artifact> selected, string destination)
    {
        var selectedPaths = selected.Select(a => a.Path).ToHashSet(FileProcessingFiles.PathComparer);
        var projected = supplied.Select(source =>
        {
            var relative = selectedPaths.Contains(source.Path) ? TargetRelativePath(source.RelativePath, step) : source.RelativePath;
            return (Source: source, Output: new Artifact(
                FileProcessingFiles.Within(Path.Combine(destination, relative), destination), relative, source.Extracted,
                step.Id, source.Origins, Path.GetDirectoryName(source.RelativePath) == Path.GetDirectoryName(relative)
                    ? source.GeneratedDirectories : null, source.ExtractionLayout));
        }).ToList();
        var selectedTargets = projected.Where(x => selectedPaths.Contains(x.Source.Path))
            .Select(x => x.Output.Path).ToHashSet(FileProcessingFiles.PathComparer);
        // Explicitly selected members are transformed exactly as requested. Unselected volume
        // companions retain their names and travel with the selected archive entry.
        var companions = CompressedFileHelper.DetectCompressedFileGroups(
                projected.Select(x => x.Output.Path).ToArray(), true, inspectFileSizes: false)
            .Where(g => g.Files.Any(selectedTargets.Contains) && g.Files.Any(FileProcessingFiles.IsVolume))
            .SelectMany(g => g.Files).ToHashSet(FileProcessingFiles.PathComparer);
        return projected.Where(x => selectedPaths.Contains(x.Source.Path) || companions.Contains(x.Output.Path)).ToList();
    }

    private static void CheckRenameProjection(FileProcessingStep step, IReadOnlyList<Artifact> active,
        IReadOnlyList<DirectoryArtifact> directories, IReadOnlyList<(Artifact Source, Artifact Output)> transformed)
    {
        var consumed = transformed.Select(x => x.Source.Path).ToHashSet(FileProcessingFiles.PathComparer);
        CheckProjection(step.Id, active.Where(a => !consumed.Contains(a.Path)).Select(PathEntry)
            .Concat(transformed.Select(x => new FileProcessingPathProjectionEntry(x.Source.RelativePath, x.Output.RelativePath)))
            .Concat(directories.Select(PathEntry)));
    }

    private static void CheckRebaseProjection(FileProcessingStep step, IReadOnlyList<Artifact> active,
        IReadOnlyList<DirectoryArtifact> directories, IReadOnlyList<Artifact> selected,
        IReadOnlyList<DirectoryArtifact> selectedDirectories)
    {
        var consumed = selected.Select(a => a.Path).ToHashSet(FileProcessingFiles.PathComparer);
        CheckProjection(step.Id, active.Where(a => !consumed.Contains(a.Path)).Select(PathEntry)
            .Concat(selected.Select(a => new FileProcessingPathProjectionEntry(a.RelativePath, RebaseRelativeDirectory(a.RelativePath, step))))
            .Concat(directories.Except(selectedDirectories).Select(PathEntry))
            .Concat(selectedDirectories.Select(d => new FileProcessingPathProjectionEntry(d.RelativePath,
                RebaseRelativeDirectory(d.RelativePath, step), true))), portable: true);
    }

    private static string RenameOperationKey(string stepId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stepId)))[..16] + "-rename";

    // Project the known prefix before capturing hashes or isolating the input. Archive contents
    // are unknown until extraction; the execution loop checks those actual outputs separately.
    private static void PreflightKnownPaths(FileProcessingPlan plan, string root, IReadOnlyList<string> files,
        IReadOnlyList<string> directories, string checkpoint, int boundaryIndex, State state)
    {
        var active = files.Select(f => new Artifact(f, Path.GetRelativePath(root, f))).ToList();
        var activeDirectories = directories.Select(d => new DirectoryArtifact(d, Path.GetRelativePath(root, d))).ToList();
        var outputs = new Dictionary<string, List<Artifact>> { ["download"] = active.ToList() };
        var directoryOutputs = new Dictionary<string, List<DirectoryArtifact>> { ["download"] = activeDirectories.ToList() };
        foreach (var step in plan.Steps.Take(boundaryIndex + 1))
        {
            if (step.Op == "extractArchive") break;
            var supplied = step.GetInputReferences().SelectMany(input => outputs[input]).ToList();
            var suppliedDirectories = step.GetInputReferences().SelectMany(input => directoryOutputs[input]).Distinct().ToList();
            if (step.GetInputReferences().Count > 1) CheckProjection(step.Id, supplied.Select(PathEntry));
            var selected = supplied.Where(a => step.Op == "rebaseDirectory"
                ? IsWithinRelativeDirectory(a.RelativePath, step.SourceDirectory!) : Matches(a, step.Selector)).ToList();
            var key = RenameOperationKey(step.Id);
            var destination = OutputDirectory(state, checkpoint, key, step, state.StepPolicies.GetValueOrDefault(step.Id));
            List<Artifact> output;
            List<DirectoryArtifact> outputDirectories;
            HashSet<string> consumed;
            if (step.Op == "rebaseDirectory")
            {
                var selectedDirectories = suppliedDirectories.Where(d =>
                    IsWithinRelativeDirectory(d.RelativePath, step.SourceDirectory!, includeSelf: true)).ToList();
                if (selected.Count == 0 && selectedDirectories.Count == 0) break;
                output = selected.Select(a => a with {RelativePath = RebaseRelativeDirectory(a.RelativePath, step),
                    Path = Path.Combine(destination, RebaseRelativeDirectory(a.RelativePath, step))}).ToList();
                outputDirectories = selectedDirectories.Select(d => d with {RelativePath = RebaseRelativeDirectory(d.RelativePath, step),
                    Path = Path.Combine(destination, RebaseRelativeDirectory(d.RelativePath, step))})
                    .Where(d => d.RelativePath.Length > 0).ToList();
                consumed = selected.Select(a => a.Path).ToHashSet(FileProcessingFiles.PathComparer);
                var retainedDirectories = activeDirectories.Except(selectedDirectories).ToList();
                CheckRebaseProjection(step, active, activeDirectories, selected, selectedDirectories);
                activeDirectories = retainedDirectories.Concat(outputDirectories).Distinct().ToList();
            }
            else
            {
                if (selected.Count == 0) break;
                var transformed = ProjectRename(step, supplied, selected, destination);
                CheckRenameProjection(step, active, activeDirectories, transformed);
                output = transformed.Select(x => x.Output).ToList();
                consumed = transformed.Select(x => x.Source.Path).ToHashSet(FileProcessingFiles.PathComparer);
                outputDirectories = suppliedDirectories;
            }
            if (!state.Operations.ContainsKey(key)) CheckUntrackedDestination(step.Id, destination, checkpoint);
            active = active.Where(a => !consumed.Contains(a.Path)).Concat(output).ToList();
            outputs[step.Id] = output;
            directoryOutputs[step.Id] = outputDirectories;
            activeDirectories = activeDirectories.Concat(outputDirectories).Distinct().ToList();
        }
    }
}
