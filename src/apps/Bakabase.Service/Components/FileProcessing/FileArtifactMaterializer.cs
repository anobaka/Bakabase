using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Abstractions.Components.FileSystem;

namespace Bakabase.Service.Components.FileProcessing;

public sealed record FileMaterializationResult(string Method, long Bytes);

/// <summary>Creates independent files, or atomically renames explicitly authorized private files.</summary>
public interface IFileArtifactMaterializer
{
    Task<FileMaterializationResult> MaterializeAsync(string source, string target, string mode, CancellationToken ct);

    /// <summary>Atomically renames an exclusively owned file without replacing a destination.
    /// False means unsupported or cross-filesystem; the source is left intact.</summary>
    Task<bool> TryMoveAsync(string source, string target, CancellationToken ct) => Task.FromResult(false);
}

/// <summary>
/// A clone is an independent copy-on-write file, never a hard link. Unsupported filesystems and
/// platforms use a regular copy; storage errors remain errors instead of being hidden by a fallback.
/// </summary>
public sealed class FileArtifactMaterializer : IFileArtifactMaterializer
{
    private readonly Func<string, string, bool>? cloneForTest;
    private readonly IStorageWriteGuard? writeGuard;
    public FileArtifactMaterializer() { }
    public FileArtifactMaterializer(IStorageWriteGuard writeGuard) => this.writeGuard = writeGuard;
    internal FileArtifactMaterializer(Func<string, string, bool> cloneForTest, IStorageWriteGuard? writeGuard = null)
    { this.cloneForTest = cloneForTest; this.writeGuard = writeGuard; }

    public Task<bool> TryMoveAsync(string source, string target, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArtifactManifestFiles.RejectLinks(source);
        ArtifactManifestFiles.RejectLinks(target);
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("The rename destination already exists.");
        // File.Move can implement EXDEV with copy/delete. The recovery journal requires one
        // atomic, exclusive rename, so use primitives that never perform that fallback.
        try
        {
            int result;
            if (OperatingSystem.IsMacOS()) result = RenameMac(source, target, 4); // RENAME_EXCL
            else if (OperatingSystem.IsLinux()) result = RenameLinux(-100, source, -100, target, 1); // RENAME_NOREPLACE
            else if (OperatingSystem.IsWindows()) result = MoveWindows(source, target, 0) ? 0 : -1;
            else return Task.FromResult(false);
            if (result == 0) return Task.FromResult(true);
            var error = Marshal.GetLastPInvokeError();
            var unsupported = OperatingSystem.IsWindows() ? error is 17 or 50 or 120 :
                OperatingSystem.IsMacOS() ? error is 18 or 22 or 45 or 78 : error is 18 or 22 or 38 or 95;
            if (unsupported) return Task.FromResult(false);
            throw new IOException($"File rename failed ({error}): {new Win32Exception(error).Message}");
        }
        catch (EntryPointNotFoundException) { return Task.FromResult(false); }
        catch (DllNotFoundException) { return Task.FromResult(false); }
    }

    public async Task<FileMaterializationResult> MaterializeAsync(string source, string target, string mode, CancellationToken ct)
    {
        if (mode is not ("auto" or "copy")) throw new ArgumentException("Choose auto or copy for file materialization.");
        ct.ThrowIfCancellationRequested();
        ArtifactManifestFiles.RejectLinks(source);
        ArtifactManifestFiles.RejectLinks(target);
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("The materialization destination already exists.");
        if (writeGuard != null) await writeGuard.CheckAsync(target, StorageWriteScope.Current, ct);
        if (mode == "auto" && cloneForTest != null)
        {
            if (cloneForTest(source, target)) return Cloned(source, target, ct);
        }
        else if (mode == "auto" && OperatingSystem.IsMacOS())
        {
            // clonefile creates dst exclusively and is atomic. Writes to either file remain private.
            // https://github.com/apple-oss-distributions/xnu/blob/main/bsd/man/man2/clonefile.2
            try
            {
                if (CloneFile(source, target, 0) == 0) return Cloned(source, target, ct);
                var error = Marshal.GetLastPInvokeError();
                if (error is not (18 or 22 or 45 or 78)) throw CloneError(error);
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
        }
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        if (mode == "auto" && cloneForTest == null && OperatingSystem.IsLinux())
        {
            // FICLONE acts on our exclusively-created destination descriptor. If unsupported,
            // copy into that same owned descriptor rather than unlinking/reopening a path.
            // https://man7.org/linux/man-pages/man2/FICLONE.2const.html
            try
            {
                if (CloneLinux(output.SafeFileHandle.DangerousGetHandle().ToInt32(), 0x40049409,
                        input.SafeFileHandle.DangerousGetHandle().ToInt32()) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    if (output.Length != input.Length) throw new IOException("The cloned file length does not match its input.");
                    await output.FlushAsync(ct);
                    return new("clone", output.Length);
                }
                var error = Marshal.GetLastPInvokeError();
                if (error is not (9 or 18 or 22 or 25 or 38 or 95)) throw CloneError(error);
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            output.SetLength(0);
            output.Position = 0;
        }
        if (writeGuard == null) await input.CopyToAsync(output, ct);
        else
        {
            // A successful clone needs no full payload budget. Once cloning is unavailable,
            // reject a known-too-large copy before its first byte; chunk checks still protect
            // against concurrent writes and changes in the live reserve during the transfer.
            await writeGuard.CheckAllocationAsync(target, input.Length, StorageWriteScope.Current, ct);
            await writeGuard.CopyFileAsync(input, output, target, StorageWriteScope.Current, ct);
        }
        await output.FlushAsync(ct);
        return new("copy", output.Length);
    }

    private static FileMaterializationResult Cloned(string source, string target, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var length = new FileInfo(target).Length;
        if (length != new FileInfo(source).Length) throw new IOException("The cloned file length does not match its input.");
        return new("clone", length);
    }
    private static IOException CloneError(int error) => new($"File clone failed ({error}): {new Win32Exception(error).Message}");

    [DllImport("libSystem.B.dylib", EntryPoint = "clonefile", SetLastError = true)]
    private static extern int CloneFile([MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string target, int flags);
    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int CloneLinux(int destination, nuint request, int source);
    [DllImport("libSystem.B.dylib", EntryPoint = "renamex_np", SetLastError = true)]
    private static extern int RenameMac([MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string target, uint flags);
    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
    private static extern int RenameLinux(int sourceDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        int targetDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string target, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindows(string source, string target, uint flags);
}
