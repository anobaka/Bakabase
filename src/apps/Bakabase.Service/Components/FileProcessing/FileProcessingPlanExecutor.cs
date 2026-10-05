using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;

namespace Bakabase.Service.Components.FileProcessing;

public record FileProcessingPlanResult(bool Completed, IReadOnlyList<string> Files, string? OutputDirectory,
    string? Message = null, string? StepId = null, bool NeedsPassword = false,
    IReadOnlyList<string>? TriedPasswords = null);

/// <summary>
/// Interprets the restricted file-processing vocabulary using the shared archive service. Each run
/// stages its own inputs and commits operation outputs atomically, so retries never rename or
/// unpack completed work again. Original downloads are retained.
/// </summary>
public sealed class FileProcessingPlanExecutor(IArchiveExtractionService extraction)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    public static void Validate(PostExtractionPlan plan) => PostExtractionPlanValidator.Validate(plan);

    public async Task<FileProcessingPlanResult> ExecuteAsync(PostExtractionPlan plan, string root,
        IReadOnlyList<string> files, string stateDirectory, string? password = null,
        Action<int>? progress = null, CancellationToken ct = default)
    {
        Validate(plan);
        if (plan.Requirement == "unknown" || (plan.Requirement == "required" && plan.Steps.Count == 0))
            return new(false, files, null, "The file-processing instructions are incomplete. Provide a plan or confirm the files were processed manually.");
        var normalized = files.Select(f => FileProcessingFiles.Within(f, root))
            .Distinct(FileProcessingFiles.PathComparer).OrderBy(x => x, FileProcessingFiles.PathComparer).ToList();
        if (normalized.Count == 0) throw new InvalidOperationException("No files were supplied for this plan.");
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(plan, Json) + "\n" + string.Join("\n", normalized)))).ToLowerInvariant();
        var directory = Path.Combine(stateDirectory, identity);
        var gate = Gates.GetOrAdd(directory, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await Run(plan, root, normalized, directory, password, progress, ct); }
        finally { gate.Release(); }
    }

    private async Task<FileProcessingPlanResult> Run(PostExtractionPlan plan, string root,
        List<string> files, string directory, string? password, Action<int>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        FileProcessingFiles.Within(directory, directory);
        var stateFile = Path.Combine(directory, "state.json");
        var state = File.Exists(stateFile)
            ? JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(stateFile, ct), Json)!
            : new State();
        async Task Save()
        {
            var temp = stateFile + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, Json), ct);
            File.Move(temp, stateFile, true);
        }
        if (state.Completed)
        {
            if (state.FinalFiles.Any(f => !File.Exists(f)))
                throw new InvalidOperationException("The completed outputs were moved or removed after this run.");
            return new(true, state.FinalFiles, Path.Combine(directory, "result"));
        }
        if (password != null && state.PendingOperation != null)
        {
            state.Passwords[state.PendingOperation] = password;
            await Save();
        }

        // Commit the entire input snapshot together; a crash leaves only our disposable staging directory.
        var inputs = Path.Combine(directory, "inputs");
        if (!Directory.Exists(inputs))
        {
            var pending = inputs + ".pending";
            ResetOwnedDirectory(pending, directory);
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(file)) throw new FileNotFoundException("An input file is no longer available.", file);
                var target = FileProcessingFiles.Within(Path.Combine(pending, Path.GetRelativePath(root, file)), pending);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await Copy(file, target, ct);
            }
            Directory.Move(pending, inputs);
        }
        if (!state.Outputs.ContainsKey("download"))
        {
            state.Outputs["download"] = FileProcessingFiles.Enumerate(inputs)
                .Select(f => new Artifact(f, Path.GetRelativePath(inputs, f))).ToList();
            state.Active = state.Outputs["download"].ToList();
            await Save();
        }

        foreach (var step in plan.Steps)
        {
            ct.ThrowIfCancellationRequested();
            if (state.Outputs.ContainsKey(step.Id)) continue;
            var supplied = state.Outputs[step.Input];
            var selected = supplied.Where(a => Matches(a, step.Selector)).ToList();
            if (selected.Count == 0)
                return new(false, state.Active.Select(x => x.Path).ToList(), null,
                    "No input files matched this step. Correct the selector before continuing.", step.Id);
            var output = new List<Artifact>();
            var consumed = new HashSet<string>(FileProcessingFiles.PathComparer);
            if (step.Op is "renameExtension" or "renameFile" or "moveFile")
            {
                // A step's inputs are immutable: a later branch may refer to download or any
                // earlier step. Commit a renamed copy set together, keeping sibling volumes in
                // the same directory so the shared archive grouper can still open them.
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(step.Id)))[..16] + "-rename";
                var destination = Path.Combine(directory, "outputs", key);
                var renamed = selected.Select(source => new Artifact(
                    FileProcessingFiles.Within(Path.Combine(destination,
                        TargetRelativePath(source.RelativePath, step)), destination),
                    TargetRelativePath(source.RelativePath, step))).ToList();
                var sourcePaths = selected.Select(a => a.Path).ToHashSet(FileProcessingFiles.PathComparer);
                EnsureUniqueResultPaths(state.Active.Where(a => !sourcePaths.Contains(a.Path))
                    .Concat(renamed).Select(a => a.RelativePath));
                if (!state.Operations.TryGetValue(key, out var operation))
                {
                    if (Directory.Exists(destination)) throw new InvalidOperationException("An untracked output directory already exists.");
                    operation = new Operation {OutputDirectory = destination};
                    state.Operations[key] = operation;
                    await Save();
                }
                if (!operation.Completed && !Directory.Exists(destination))
                {
                    var pending = destination + ".pending";
                    ResetOwnedDirectory(pending, directory);
                    for (var i = 0; i < selected.Count; i++)
                    {
                        var target = FileProcessingFiles.Within(Path.Combine(pending, renamed[i].RelativePath), pending);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        await Copy(selected[i].Path, target, ct);
                    }
                    Directory.Move(pending, destination);
                }
                operation.Completed = true;
                await Save();
                foreach (var source in selected) consumed.Add(source.Path);
                output.AddRange(renamed);
            }
            else
            {
                var groups = CompressedFileHelper.DetectCompressedFileGroups(selected.Select(a => a.Path).ToArray(), true);
                if (groups.Count == 0)
                    return new(false, state.Active.Select(x => x.Path).ToList(), null,
                        "No supported archive matched this step. Check the extension or select files explicitly.", step.Id);
                FileProcessingFiles.ValidateVolumes(selected.Select(x => x.Path));
                for (var i = 0; i < groups.Count; i++)
                {
                    var group = groups[i];
                    var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(step.Id)))[..16] + "-" + i;
                    var destination = Path.Combine(directory, "outputs", key);
                    if (!state.Operations.TryGetValue(key, out var operation))
                    {
                        if (Directory.Exists(destination)) throw new InvalidOperationException("An untracked output directory already exists.");
                        operation = new Operation {OutputDirectory = destination};
                        state.Operations[key] = operation;
                        await Save();
                    }
                    if (!operation.Completed && !Directory.Exists(destination))
                    {
                        var candidates = new List<string?>();
                        if (state.Passwords.TryGetValue(key, out var answered)) candidates.Add(answered);
                        candidates.Add(step.Password);
                        candidates = candidates.Distinct().ToList();
                        var probe = await extraction.ProbePasswordAsync(group.Files[0], candidates, null, ct);
                        if (!probe.Succeeded)
                        {
                            state.PendingOperation = key;
                            await Save();
                            return new(false, state.Active.Select(x => x.Path).ToList(), null,
                                $"Could not open {Path.GetFileName(group.Files[0])}. Check the password and all archive parts.",
                                step.Id, true, candidates.Where(x => x != null).Cast<string>().ToList());
                        }
                        var pending = destination + ".pending";
                        ResetOwnedDirectory(pending, directory);
                        var result = await extraction.ExtractAsync(new ArchiveExtractionRequest(group.Files,
                            pending, probe.Password, DecompressToNewFolder: false), progress, ct);
                        if (!result.Succeeded) throw new InvalidOperationException(result.Message);
                        // Reject symlink outputs before making them available to subsequent steps.
                        FileProcessingFiles.Enumerate(pending);
                        Directory.Move(pending, destination);
                    }
                    operation.Completed = true;
                    state.PendingOperation = null;
                    await Save();
                    foreach (var file in group.Files) consumed.Add(file);
                    var entry = selected.First(a => a.Path == group.Files[0]);
                    var prefix = Path.Combine(Path.GetDirectoryName(entry.RelativePath) ?? "",
                        Path.GetFileNameWithoutExtension(entry.RelativePath));
                    output.AddRange(FileProcessingFiles.Enumerate(destination).Select(f =>
                        new Artifact(f, Path.Combine(prefix, Path.GetRelativePath(destination, f)))));
                }
            }
            // Keep unrelated files and siblings; following steps can only see their declared input's results.
            state.Active = state.Active.Where(a => !consumed.Contains(a.Path)).Concat(output).ToList();
            state.Outputs[step.Id] = output;
            await Save();
        }

        var resultDirectory = Path.Combine(directory, "result");
        if (!Directory.Exists(resultDirectory))
        {
            var pending = resultDirectory + ".pending";
            ResetOwnedDirectory(pending, directory);
            foreach (var artifact in state.Active)
            {
                var target = FileProcessingFiles.Within(Path.Combine(pending, artifact.RelativePath), pending);
                if (File.Exists(target)) throw new InvalidOperationException("Two outputs have the same relative file name.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await Copy(artifact.Path, target, ct);
            }
            Directory.Move(pending, resultDirectory);
        }
        state.FinalFiles = FileProcessingFiles.Enumerate(resultDirectory);
        state.Completed = true;
        await Save();
        return new(true, state.FinalFiles, resultDirectory);
    }

    private static bool Matches(Artifact artifact, string? selector) => string.IsNullOrWhiteSpace(selector) ||
        FileSystemName.MatchesSimpleExpression(selector.Replace('\\', '/'),
            selector.Contains('/') || selector.Contains('\\') ? artifact.RelativePath.Replace('\\', '/') : Path.GetFileName(artifact.Path),
            ignoreCase: OperatingSystem.IsWindows());

    private static string TargetRelativePath(string path, PostExtractionStep step) => step.Op switch
    {
        "renameExtension" => ChangeExtension(path, step.Extension!),
        "renameFile" => Path.Combine(Path.GetDirectoryName(path) ?? "", step.TargetName!),
        "moveFile" => Path.Combine(step.TargetDirectory == "." ? "" :
            step.TargetDirectory!.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar),
            Path.GetFileName(path)),
        _ => throw new InvalidOperationException("Unsupported file operation.")
    };

    private static void EnsureUniqueResultPaths(IEnumerable<string> paths)
    {
        var files = new HashSet<string>(FileProcessingFiles.PathComparer);
        foreach (var path in paths)
            if (!files.Add(path.Replace('\\', '/')))
                throw new InvalidOperationException("The operation would overwrite another output file.");
        foreach (var path in files)
        {
            var slash = path.LastIndexOf('/');
            while (slash >= 0)
            {
                if (files.Contains(path[..slash]))
                    throw new InvalidOperationException("An output file conflicts with a required directory.");
                slash = slash == 0 ? -1 : path.LastIndexOf('/', slash - 1);
            }
        }
    }

    private static string ChangeExtension(string path, string extension)
    {
        var volume = Regex.Match(path, @"\.\d{3,}$");
        var basis = volume.Success ? path[..volume.Index] : path;
        return Path.ChangeExtension(basis, extension.TrimStart('.')) + (volume.Success ? volume.Value : "");
    }

    private static void ResetOwnedDirectory(string path, string root)
    {
        FileProcessingFiles.Within(path, root);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
    }

    private static async Task Copy(string source, string target, CancellationToken ct)
    {
        await using var input = File.OpenRead(source);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, ct);
        await output.FlushAsync(ct);
    }

    public sealed record Artifact(string Path, string RelativePath);
    public sealed class Operation
    {
        public string? OutputDirectory { get; set; }
        public bool Completed { get; set; }
    }
    public sealed class State
    {
        public Dictionary<string, List<Artifact>> Outputs { get; set; } = [];
        public List<Artifact> Active { get; set; } = [];
        public Dictionary<string, Operation> Operations { get; set; } = [];
        public Dictionary<string, string> Passwords { get; set; } = [];
        public string? PendingOperation { get; set; }
        public bool Completed { get; set; }
        public List<string> FinalFiles { get; set; } = [];
    }
}
