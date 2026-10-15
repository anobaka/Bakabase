using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Federation.Identity;

namespace Bakabase.Service.Components.Federation.Processing;

/// <summary>Temporary, content-addressed artifacts. Separate from live attachment storage until the
/// owner's revision check succeeds. Payloads never enter job JSON or a full-memory ORM cache.</summary>
public sealed class ResourceProcessingArtifacts(IFederationDataDirectory directory)
{
    public static string Key(ResourceProcessingPrepared prepared) => ResourceProcessingWire.Hash(new
        {prepared.Input.OwnerId, prepared.Input.OwnerEpoch, prepared.Input.JobId, prepared.Token, prepared.Input.RuleHash});
    public string GetDirectory(ResourceProcessingPrepared prepared, string area) => Path.Combine(directory.Ensure(),
        "processing", area, Key(prepared));
    public void CleanupOwnerKey(string? key)
    {
        if (key == null) return;
        if (key.Length != 64 || !ResourceProcessingWire.IsHex(key))
            throw new ResourceProcessingException("InvalidArtifact", "The artifact directory key is invalid.");
        var path = Path.Combine(directory.Ensure(), "processing", "owner", key);
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
    public IFileManager CreateWorkerFileManager(ResourceProcessingPrepared prepared, CancellationToken ct) =>
        new ProcessingFileManager(Path.Combine(GetDirectory(prepared, "worker"), "work"), ct);

    public async Task<ResourceProcessingArtifact> CaptureAsync(ResourceProcessingPrepared prepared, string path,
        string allowedRoot, bool isResourceFile, CancellationToken ct)
    {
        // Called only after either job-root or mapped-resource-root containment and storage checks.
        ResourceProcessingPaths.Relative(allowedRoot, isResourceFile, path);
        var info = new FileInfo(path);
        if (info.Length > ResourceProcessingWire.MaxArtifactBytes)
            throw new ResourceProcessingException("BudgetExceeded", "An enhancement attachment exceeds 16 MiB.");
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).ToLowerInvariant();
        var artifact = new ResourceProcessingArtifact(hash, Path.GetFileName(path), info.Length);
        var destination = ArtifactPath(prepared, "worker", hash);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!File.Exists(destination))
        {
            input.Position = 0;
            var temp = destination + ".tmp";
            try
            {
                await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                                 64 * 1024, FileOptions.Asynchronous))
                    await CopyBounded(input, output, artifact.Length, ct);
                File.Move(temp, destination, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        return artifact;
    }

    public FileStream OpenWorkerArtifact(ResourceProcessingPrepared prepared, string hash) =>
        new(ArtifactPath(prepared, "worker", hash), FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    public async Task<long> AppendOwnerChunkAsync(ResourceProcessingPrepared prepared,
        ResourceProcessingArtifact artifact, long offset, string base64, CancellationToken ct)
    {
        Validate(artifact);
        if (base64 == null || base64.Length > (ResourceProcessingWire.ArtifactChunkBytes + 2) / 3 * 4)
            throw new ResourceProcessingException("BudgetExceeded", "The artifact chunk is too large.");
        var bytes = Convert.FromBase64String(base64);
        if (offset < 0 || bytes.Length > ResourceProcessingWire.ArtifactChunkBytes || offset + bytes.Length > artifact.Length)
            throw new ResourceProcessingException("InvalidArtifact", "The artifact chunk is out of bounds.");
        var path = ArtifactPath(prepared, "owner", artifact.Hash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // FileShare.None serializes concurrent chunks without retaining an unbounded lock dictionary.
        await using var output = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            64 * 1024, FileOptions.Asynchronous);
        if (offset < output.Length && offset + bytes.Length <= output.Length)
        {
            output.Position = offset;
            var old = new byte[bytes.Length];
            await output.ReadExactlyAsync(old, ct);
            if (!old.AsSpan().SequenceEqual(bytes)) throw new ResourceProcessingException("ArtifactConflict", "An artifact retry contained different bytes.");
            return output.Length;
        }
        if (output.Length != offset) throw new ResourceProcessingException("ArtifactOffset", "Resume from the stored artifact offset.");
        output.Position = offset;
        await output.WriteAsync(bytes, ct);
        await output.FlushAsync(ct);
        output.Flush(true);
        if (output.Length == artifact.Length)
        {
            output.Position = 0;
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(output, ct)).ToLowerInvariant();
            if (!string.Equals(actual, artifact.Hash, StringComparison.OrdinalIgnoreCase))
            {
                output.SetLength(0);
                throw new ResourceProcessingException("ArtifactHash", "The artifact content did not match its manifest.");
            }
        }
        return output.Length;
    }

    public async Task<string> InstallOwnerArtifactAsync(ResourceProcessingPrepared prepared,
        ResourceProcessingArtifact artifact, IFileManager ownerFiles, CancellationToken ct)
    {
        Validate(artifact);
        var source = ArtifactPath(prepared, "owner", artifact.Hash);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length != artifact.Length || !string.Equals(
                Convert.ToHexString(await SHA256.HashDataAsync(input, ct)), artifact.Hash, StringComparison.OrdinalIgnoreCase))
            throw new ResourceProcessingException("ArtifactHash", "The complete verified artifact has not arrived yet.");
        var extension = Path.GetExtension(artifact.FileName);
        if (extension.Length > 16 || extension.Any(c => c != '.' && !char.IsAsciiLetterOrDigit(c))) extension = "";
        var target = ownerFiles.GetEnhancerResourceFilePath("distributed", prepared.Input.ResourceId.ToString(), artifact.Hash + extension);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            await using var existing = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (existing.Length == artifact.Length && string.Equals(
                    Convert.ToHexString(await SHA256.HashDataAsync(existing, ct)), artifact.Hash, StringComparison.OrdinalIgnoreCase))
                return target; // Reuse content already referenced by the library, including open Windows covers.
        }
        input.Position = 0;
        var temp = target + "." + prepared.Input.JobId + ".tmp";
        try
        {
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024,
                             FileOptions.Asynchronous)) await CopyBounded(input, output, artifact.Length, ct);
            File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return target;
    }

    public void Cleanup(ResourceProcessingPrepared prepared, string area)
    {
        var path = GetDirectory(prepared, area);
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
    public static void Validate(ResourceProcessingArtifact artifact)
    {
        if (artifact == null || artifact.Hash is not {Length: 64} || !ResourceProcessingWire.IsHex(artifact.Hash) ||
            artifact.Length is < 0 or > ResourceProcessingWire.MaxArtifactBytes || string.IsNullOrEmpty(artifact.FileName) ||
            artifact.FileName.Length > 255 || artifact.FileName.IndexOfAny(['/', '\\', '\0']) >= 0)
            throw new ResourceProcessingException("InvalidArtifact", "The artifact manifest is invalid.");
    }
    private string ArtifactPath(ResourceProcessingPrepared prepared, string area, string hash)
    {
        if (hash is not {Length: 64} || !ResourceProcessingWire.IsHex(hash))
            throw new ResourceProcessingException("InvalidArtifact", "The artifact hash is invalid.");
        return Path.Combine(GetDirectory(prepared, area), hash.ToLowerInvariant());
    }
    private static async Task CopyBounded(Stream source, Stream target, long expected, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, ct)) != 0)
        {
            total += count;
            if (total > expected) throw new ResourceProcessingException("ArtifactChanged", "The artifact changed while being transferred.");
            await target.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        if (total != expected) throw new ResourceProcessingException("ArtifactChanged", "The artifact changed while being transferred.");
    }

    private sealed class ProcessingFileManager(string root, CancellationToken cancellationToken) : IFileManager
    {
        private long _savedBytes;
        public string BaseDir { get; } = Path.GetFullPath(root);
        public string BuildAbsolutePath(params object[] segmentsAfterBaseDir)
        {
            var path = Path.GetFullPath(Path.Combine(new[] {BaseDir}.Concat(segmentsAfterBaseDir.Select(s => s.ToString()!)).ToArray()));
            var relative = Path.GetRelativePath(BaseDir, path).Replace('\\', '/');
            ResourceProcessingPaths.ValidateRelative(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }
        public async Task<string> Save(string path, byte[] data, CancellationToken ct)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ct.ThrowIfCancellationRequested();
            if (data.Length > ResourceProcessingWire.MaxArtifactBytes ||
                Interlocked.Add(ref _savedBytes, data.Length) > ResourceProcessingWire.MaxTotalArtifactBytes)
                throw new ResourceProcessingException("BudgetExceeded", "Enhancement attachments exceed the job budget.");
            var full = Path.IsPathRooted(path) ? BuildAbsolutePath(Path.GetRelativePath(BaseDir, path)) : BuildAbsolutePath(path);
            await File.WriteAllBytesAsync(full, data, cancellationToken);
            return full;
        }
        public string GetLocalCoverPathWithoutExtension(int resourceId) => BuildAbsolutePath("covers", "local", resourceId);
        public string GetSourceCoverDir(string source, int resourceId) => BuildAbsolutePath("covers", source, resourceId);
        public string GetManualCoverPath(int resourceId, int index) => BuildAbsolutePath("covers", $"{resourceId}-{index}.jpg");
        public string GetEnhancerFilePath(string enhancerId, string fileName) => BuildAbsolutePath("enhancers", enhancerId, fileName);
        public string GetEnhancerResourceFilePath(string enhancerId, string resourceIdPart, string fileName) => BuildAbsolutePath("enhancers", enhancerId, resourceIdPart, fileName);
        public string GetAttachmentPath(string fileName) => BuildAbsolutePath("attachments", fileName);
        public string GetAigcGeneratorDir(int generatorId) => BuildAbsolutePath("aigc", generatorId);
        public string GetAigcRunDir(int generatorId, int runId) => BuildAbsolutePath("aigc", generatorId, runId);
        public string GetAigcArtifactRelativePath(int generatorId, int runId, string fileName) => Path.GetRelativePath(BaseDir, BuildAbsolutePath("aigc", generatorId, runId, fileName));
    }
}
