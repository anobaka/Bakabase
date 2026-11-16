using System;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Bounded, already-redacted call documents. Only the exact workflow receipt can
/// address a document; user input never supplies a path or a global audit-log identifier.</summary>
public sealed class AssessmentCallSnapshotStore
{
    internal sealed record Snapshot(string ArtifactId, int Iteration,
        AssessmentInspectionText.Document? Input, AssessmentInspectionText.Document? Output);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    private readonly string _root;

    public AssessmentCallSnapshotStore(AppService app) : this(app.AppDataDirectory) { }
    internal AssessmentCallSnapshotStore(string appData) => _root = Path.Combine(appData, "file-processing", "assessment-calls");

    internal async Task<string> SaveAsync(string artifact, int iteration, string? input, string? output,
        CancellationToken ct)
    {
        var data = new Snapshot(artifact, iteration,
            input == null ? null : AssessmentInspectionText.Sanitize(input),
            output == null ? null : AssessmentInspectionText.Sanitize(output));
        Directory.CreateDirectory(_root);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(_root, id + ".json");
        try
        {
            var options = new FileStreamOptions {Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                Options = FileOptions.Asynchronous};
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using var stream = new FileStream(path, options);
            await JsonSerializer.SerializeAsync(stream, data, Json, ct);
            return id;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }

    internal async Task<Snapshot?> ReadAsync(string? id, string artifact, int iteration, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return null;
        var path = Path.Combine(_root, id + ".json");
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.LinkTarget != null || info.Length > 6L * AssessmentInspectionText.DocumentByteLimit) return null;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            var data = await JsonSerializer.DeserializeAsync<Snapshot>(stream, Json, ct);
            if (data?.ArtifactId != artifact || data.Iteration != iteration) return null;
            // A snapshot is a diagnostic cache, not a trusted bypass for redaction.
            static AssessmentInspectionText.Document? Checked(AssessmentInspectionText.Document? document)
            {
                if (document == null) return null;
                var safe = AssessmentInspectionText.Sanitize(document.Text);
                return safe with {Truncated = safe.Truncated || document.Truncated, Redacted = safe.Redacted || document.Redacted};
            }
            return data with {Input = Checked(data.Input), Output = Checked(data.Output)};
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}
