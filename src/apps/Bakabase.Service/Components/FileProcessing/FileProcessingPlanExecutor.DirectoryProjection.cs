using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingPlanExecutor
{
    // The caller supplies the exact manifest prefix. No managed-name guessing, directory
    // discovery or flattening takes place here; sibling entries retain their original paths.
    private static bool IsWithinRelativeDirectory(string path, string prefix, bool includeSelf = false)
    {
        path = path.Replace('\\', '/'); prefix = prefix.Replace('\\', '/');
        return includeSelf && path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal);
    }

    private static string RebaseRelativeDirectory(string path, FileProcessingStep step)
    {
        path = path.Replace('\\', '/');
        var prefix = step.SourceDirectory!.Replace('\\', '/');
        var suffix = path == prefix ? "" : path[(prefix.Length + 1)..];
        var target = step.TargetDirectory == "." ? "" : step.TargetDirectory!.Replace('\\', '/');
        return target.Length == 0 ? suffix : suffix.Length == 0 ? target : target + "/" + suffix;
    }

    private async Task<(List<Artifact> Files, List<DirectoryArtifact> Directories)> RebaseDirectory(
        FileProcessingPlan plan, FileProcessingStep step, List<Artifact> selected,
        List<DirectoryArtifact> selectedDirectories, State state, string directory,
        ArtifactStoragePolicy? policy, Func<Task> save, CancellationToken ct)
    {
        if (state.DirectoryVersion != 1 || state.LineageVersion != 1)
            throw new InvalidDataException("Directory projection requires a complete registered file and directory manifest.");
        foreach (var item in selectedDirectories)
        {
            FileProcessingFiles.Within(item.Path, directory);
            ArtifactManifestFiles.RejectLinks(item.Path);
            if (!File.GetAttributes(item.Path).HasFlag(FileAttributes.Directory))
                throw new InvalidDataException("A registered source directory changed before projection.");
        }
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(step.Id)))[..16] + "-rename";
        var destination = OutputDirectory(state, directory, key, step, policy);
        var output = selected.Select(source =>
        {
            var relative = RebaseRelativeDirectory(source.RelativePath, step);
            return source with {Path = FileProcessingFiles.Within(Path.Combine(destination, relative), destination),
                RelativePath = relative, StepId = step.Id,
                GeneratedDirectories = FileArtifactDeliveryLayout.RebaseAncestors(source.GeneratedDirectories,
                    step.SourceDirectory!, step.TargetDirectory!)};
        }).ToList();
        // An explicitly empty source directory mapped to the output root is represented by
        // that root itself, not an invalid empty relative directory entry.
        var directories = selectedDirectories.Select(source => (Source: source, Relative: RebaseRelativeDirectory(source.RelativePath, step)))
            .Where(item => item.Relative.Length > 0).DistinctBy(item => item.Relative)
            .Select(item => new DirectoryArtifact(FileProcessingFiles.Within(Path.Combine(destination, item.Relative), destination), item.Relative,
                FileArtifactDeliveryLayout.RebaseAncestors(item.Source.GeneratedDirectories, step.SourceDirectory!, step.TargetDirectory!))).ToList();
        CheckRebaseProjection(step, state.Active, state.ActiveDirectories, selected, selectedDirectories);
        var projection = selected.Zip(output).Select(pair => (pair.First.Path, pair.Second.RelativePath)).ToList();
        var projectedDirectories = directories.Select(d => d.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var created = false;
        if (!state.Operations.TryGetValue(key, out var operation))
        {
            CheckUntrackedDestination(step.Id, destination, directory);
            var manifest = selected.Count == 0 ? new ArtifactManifest([], ArtifactManifestFiles.Fingerprint([])) :
                await ArtifactManifestFiles.CaptureAsync(directory, selected.Select(a => a.Path).ToArray(), ct);
            var entries = manifest.Entries.ToDictionary(e => FileProcessingFiles.Within(Path.Combine(directory, e.RelativePath), directory),
                FileProcessingFiles.PathComparer);
            operation = new Operation {OutputDirectory = destination, ProjectedDirectories = projectedDirectories,
                RenameFiles = selected.Zip(output).Select(pair => new RenameFile(pair.First.Path, pair.Second.RelativePath,
                    entries[pair.First.Path], CanMovePrivateInput(plan, step, state, directory, pair.First.Path, policy))).ToList()};
            ResetOwnedDirectory(destination + ".pending", directory);
            state.Operations[key] = operation;
            await save();
            created = true;
        }
        if (operation.RenameFiles == null || operation.ProjectedDirectories == null ||
            !operation.ProjectedDirectories.SequenceEqual(projectedDirectories, StringComparer.Ordinal))
            throw new InvalidDataException("The directory projection journal no longer matches its manifest.");
        await ExecuteRenameJournal(operation, projection, directory, MaterializationMode(state, policy), !created, ct);
        if (output.Count > 0)
            await CaptureContents(state, destination, output.Select(a => a.Path).ToArray(), ct,
                directories.Select(d => d.Path).ToArray());
        foreach (var pair in selected.Zip(output))
        {
            if (!SameContent(state.FileContents[ContentKey(state, pair.First.Path)], state.FileContents[ContentKey(state, pair.Second.Path)]))
                throw new InvalidDataException("A projected file differs from its registered source.");
            AddTransition(state, pair.First.Path, [pair.Second.Path]);
        }
        operation.Completed = true;
        await save();
        return (output, directories);
    }

    private static void CheckProjectedDirectories(string root, Operation operation, bool complete)
    {
        if (operation.ProjectedDirectories == null) return; // Preserve legacy rename journals.
        var expected = new HashSet<string>(FileProcessingFiles.PathComparer);
        foreach (var relative in operation.RenameFiles!.Select(entry => entry.RelativePath)
                     .Select(Path.GetDirectoryName).Where(path => !string.IsNullOrEmpty(path))
                     .Concat(operation.ProjectedDirectories))
        {
            for (var path = relative; !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
                expected.Add(FileProcessingFiles.Within(Path.Combine(root, path), root));
        }
        var actual = new HashSet<string>(FileProcessingFiles.PathComparer);
        void Visit(string current)
        {
            ArtifactManifestFiles.RejectLinks(current);
            foreach (var child in Directory.EnumerateDirectories(current))
            {
                ArtifactManifestFiles.RejectLinks(child);
                actual.Add(child); Visit(child);
            }
        }
        Visit(root);
        if (complete ? !actual.SetEquals(expected) : !actual.IsSubsetOf(expected))
            throw new InvalidDataException("The directory projection contains unexpected or missing directories.");
    }

    private static void CreateProjectedDirectories(string root, Operation operation)
    {
        foreach (var relative in operation.ProjectedDirectories ?? [])
        {
            var path = FileProcessingFiles.Within(Path.Combine(root, relative), root);
            ArtifactManifestFiles.RejectLinks(path);
            Directory.CreateDirectory(path);
            ArtifactManifestFiles.RejectLinks(path);
        }
    }
}
