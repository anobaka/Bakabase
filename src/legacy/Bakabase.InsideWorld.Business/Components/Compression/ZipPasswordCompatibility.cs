using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Exceptions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;

namespace Bakabase.InsideWorld.Business.Components.Compression;

/// <summary>Isolated mature ZIP reader. Passwords travel only through stdin; output is a closed protocol.</summary>
internal static class ZipPasswordCompatibility
{
    private static readonly Lazy<string> Script = new(() =>
    {
        using var stream = typeof(ZipPasswordCompatibility).Assembly.GetManifestResourceStream("Bakabase.ZipPasswordBridge.main.py")
            ?? throw new InvalidOperationException("The ZIP compatibility adapter is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
    internal sealed record Result(bool Ok, string? Code = null, List<CompressedFileEntry>? Entries = null,
        int? Progress = null, int? FileCount = null, long? TotalBytes = null);

    internal static async Task<Result> RunAsync(PythonRuntimeExecution execution, string operation, string path,
        string? destination, string? password, ArchiveOpenOptions options, Action<string>? progress, CancellationToken ct)
    {
        if (options.TypeArgument != null || options.Mode != null)
            throw new ArchivePasswordEncodingException("ArchivePasswordEncodingUnsupported");
        var start = execution.StartInfo(["-I", "-c", Script.Value], Path.GetDirectoryName(Path.GetFullPath(path))!);
        var proof = options.ZipStructure;
        var request = JsonSerializer.Serialize(new {operation, path = Path.GetFullPath(path), destination,
            password, passwordCodePage = options.ZipPasswordCodePage is { } passwordCodePage ? (int)passwordCodePage : 65001,
            structure = proof == null ? null : new {sourceLength = proof.SourceLength, zipOffset = proof.ZipOffset,
                centralDirectoryOffset = proof.CentralDirectoryOffset, centralDirectorySize = proof.CentralDirectorySize,
                entryCount = proof.EntryCount, archiveEnd = proof.ArchiveEnd, zip64 = proof.Zip64,
                tailSha256 = proof.TailSha256, centralSha256 = proof.CentralSha256},
            fileNameCodePage = options.ZipFileNameCodePage is { } nameCodePage ? (int?)nameCodePage : null});
        return await RunProcessAsync(start, request, progress, ct, observeWrites: operation == "extract");
    }

    internal static async Task<Result> RunProcessAsync(ProcessStartInfo start, string request,
        Action<string>? progress, CancellationToken ct, bool? observeWrites = null)
    {
        ct.ThrowIfCancellationRequested();
        using var process = new Process {StartInfo = start};
        process.Start();
        var exited = process.WaitForExitAsync(CancellationToken.None);
        var observe = observeWrites.HasValue ? ArchiveProcessIo.ObserveAsync(process.Id, exited, observeWrites.Value) : Task.CompletedTask;
        using var registration = ct.Register(() => Kill(process));
        var output = ReadOutput(process.StandardOutput, progress, ct);
        var errors = Drain(process.StandardError);
        try
        {
            await process.StandardInput.WriteAsync(request.AsMemory(), ct);
            process.StandardInput.Close();
            // A bounded output failure must terminate its writer rather than leave a blocked child.
            var finished = process.WaitForExitAsync(ct);
            if (await Task.WhenAny(finished, output) == output && output.IsFaulted) Kill(process);
            await finished;
            var result = await output;
            ct.ThrowIfCancellationRequested();
            if (result == null || process.ExitCode == 0 != result.Ok)
                throw new ArchivePasswordEncodingException("ArchiveIntegrityFailure");
            return result;
        }
        finally
        {
            if (!process.HasExited) Kill(process);
            await exited;
            await observe;
            await errors;
            try { await output; } catch when (ct.IsCancellationRequested) { }
        }
    }

    private static async Task<Result?> ReadOutput(StreamReader reader, Action<string>? progress, CancellationToken ct)
    {
        const int maximumCharacters = 24 * 1024 * 1024;
        var buffer = new char[8192];
        var line = new StringBuilder();
        var total = 0;
        Result? result = null;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), ct);
            if (count == 0) break;
            total = checked(total + count);
            if (total > maximumCharacters) throw new InvalidDataException("ZIP adapter output exceeded its bound.");
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] != '\n') {line.Append(buffer[i]); continue;}
                var value = JsonSerializer.Deserialize<Result>(line.ToString(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidDataException("ZIP adapter output was empty.");
                line.Clear();
                if (value.Progress is { } percent)
                {
                    if (result != null || percent is < 0 or > 100) throw new InvalidDataException("ZIP adapter progress was invalid.");
                    progress?.Invoke(percent + "%");
                }
                else
                {
                    if (result != null) throw new InvalidDataException("ZIP adapter returned multiple results.");
                    result = value;
                }
            }
        }
        if (line.Length != 0) throw new InvalidDataException("ZIP adapter returned incomplete output.");
        return result;
    }

    private static async Task Drain(StreamReader reader)
    {
        // Drain without retaining dependency tracebacks or accidentally surfacing secrets.
        var buffer = new char[8192];
        while (await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None) != 0) { }
    }
    private static void Kill(Process process)
    {
        try {process.Kill(entireProcessTree: true);}
        catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
    }

    internal static void RequireSuccess(Result result)
    {
        if (result.Ok) return;
        if (result.Code == "ArchiveCompatibilityDependencyMissing")
            throw new DependencyNotInstalledException("Pyzipper", "pyzipper", "Install or repair pyzipper in dependency management for the selected Python runtime.", PyzipperService.ComponentId);
        throw new ArchivePasswordEncodingException(result.Code ?? "ArchiveIntegrityFailure");
    }
}

public sealed class ArchivePasswordEncodingException : InvalidOperationException
{
    public string Code { get; }
    public ArchivePasswordEncodingException(string code) : base(code switch
    {
        "ArchivePasswordEncodingUnsupported" => "This ZIP layout, encryption or compression method is outside the compatibility reader's validated scope. Only ordinary single-volume ZIP or an independently verified single-disk ZIP/ZIP64 wrapper is supported, with stored or deflate entries. Keep the input and use a supported extraction step.",
        "ArchivePasswordNotRepresentable" => "The supplied password cannot be represented in the selected ZIP password encoding. Choose its original encoding; the password was not changed.",
        "ArchiveExpansionLimit" => "ZIP compatibility extraction exceeded its file count, catalog, or expanded-size limit. The source was retained.",
        "ArchiveUnsafeOutput" => "ZIP compatibility extraction rejected an unsafe, linked, colliding, or nonempty output path. The source was retained.",
        "ArchiveStorageUnavailable" => "The ZIP input or private output storage could not be read or written. Reconnect storage before retrying.",
        _ => "ZIP compatibility validation failed authentication, CRC, size, or container consistency checks. The source was retained."
    }) => Code = code;
}
