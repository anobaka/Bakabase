using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

internal static class RcloneProcess
{
    internal static async Task<string> RunAsync(ProcessStartInfo info, string? input,
        Action<RcloneCopyProgress>? progress, CancellationToken ct, Func<string, string>? classifyFailure = null)
    {
        ct.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new RcloneRemoteException("RCLONE_START_FAILED");
        void Kill() { try { if (!process.HasExited) process.Kill(true); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
        using var registration = ct.Register(Kill);
        var stdout = ReadOutputAsync(process.StandardOutput, Kill);
        var stderr = ReadLogAsync(process.StandardError, progress, classifyFailure != null);
        try
        {
            if (input != null) await process.StandardInput.WriteLineAsync(input.AsMemory(), ct);
            process.StandardInput.Close();
            await process.WaitForExitAsync(ct);
            var output = await stdout; var diagnostic = await stderr;
            ct.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new RcloneRemoteException(classifyFailure?.Invoke(diagnostic) ?? "RCLONE_REMOTE_FAILED");
            return output;
        }
        finally
        {
            Kill();
            await process.WaitForExitAsync(CancellationToken.None);
            // Observe pipe failures, while preserving the original cancellation/error.
            try { await Task.WhenAll(stdout, stderr); } catch { }
        }
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, Action kill)
    {
        var output = new StringBuilder(); var buffer = new char[8192]; int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            if (output.Length + count > 32 * 1024 * 1024)
            { kill(); throw new RcloneRemoteException("RCLONE_OUTPUT_LIMIT"); }
            output.Append(buffer, 0, count);
        }
        return output.ToString();
    }

    private static async Task<string> ReadLogAsync(StreamReader reader, Action<RcloneCopyProgress>? progress, bool collectDiagnostic)
    {
        // Only configuration requests opt into a bounded in-memory diagnostic. It is
        // classified to fixed codes and is never placed in an exception, DTO or log.
        var diagnostic = collectDiagnostic ? new StringBuilder() : null;
        var buffer = new char[4096]; var line = new StringBuilder(); var oversized = false; int count;
        void Emit()
        {
            if (!oversized && progress != null)
            {
                try
                {
                    using var json = JsonDocument.Parse(line.ToString());
                    if (json.RootElement.TryGetProperty("stats", out var stats))
                    {
                        long Value(string name) => stats.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? Math.Max(0, n) : 0;
                        // No upstream text, account name, file path, remote URL or credentials.
                        try { progress(new(Value("bytes"), Value("totalBytes"), Value("transfers"), Value("totalTransfers"))); }
                        catch { /* Observers cannot stop draining the child process pipes. */ }
                    }
                }
                catch (JsonException) { }
                catch (InvalidOperationException) { }
            }
            line.Clear(); oversized = false;
        }
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            if (diagnostic != null && diagnostic.Length < 65536)
                diagnostic.Append(buffer, 0, Math.Min(count, 65536 - diagnostic.Length));
            for (var i = 0; i < count; i++)
                if (buffer[i] == '\n') Emit();
                else if (!oversized && line.Length < 65536) line.Append(buffer[i]);
                else { oversized = true; line.Clear(); }
        }
        if (line.Length != 0) Emit();
        return diagnostic?.ToString() ?? "";
    }
}
