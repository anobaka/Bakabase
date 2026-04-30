using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Relocation;
using Microsoft.Data.Sqlite;

namespace Bakabase.Tests.Relocation;

/// <summary>
/// Spins up a temporary filesystem with the layout the runner expects: an anchor dir holding
/// <c>app.json</c>, a current data dir (which may equal the anchor), and a target dir. Tests
/// seed files + a marker, run the runner, and assert against the result.
/// </summary>
public sealed class RelocationTestHarness : IDisposable
{
    public string Root { get; }
    public string AnchorDir { get; }
    public string CurrentDataDir { get; private set; }
    public string TargetDir { get; }

    /// <summary>
    /// In-memory mirror of <c>app.json.DataPath</c> — the runner mutates this through the
    /// save-data-path callback. Tests assert against it.
    /// </summary>
    public string? PersistedDataPath { get; private set; }

    /// <summary>
    /// Mirror of <c>app.json.PrevDataPath</c> — set when the runner reports that data was
    /// physically moved (CopyToEmpty / OverwriteTarget), null for UseTarget.
    /// </summary>
    public string? PersistedPrevDataPath { get; private set; }

    public RelocationTestHarness()
    {
        Root = Path.Combine(Path.GetTempPath(), "bakabase-runner-" + Guid.NewGuid().ToString("N"));
        AnchorDir = Path.Combine(Root, "anchor");
        TargetDir = Path.Combine(Root, "target");
        CurrentDataDir = AnchorDir;
        Directory.CreateDirectory(AnchorDir);
    }

    public RelocationTestHarness WithCurrentDataDirAt(string relativeName)
    {
        CurrentDataDir = Path.Combine(Root, relativeName);
        Directory.CreateDirectory(CurrentDataDir);
        PersistedDataPath = CurrentDataDir;
        return this;
    }

    public RelocationTestHarness WithFile(string relPath, byte[] content)
    {
        var full = Path.Combine(CurrentDataDir, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return this;
    }

    public RelocationTestHarness WithFile(string relPath, string content) =>
        WithFile(relPath, System.Text.Encoding.UTF8.GetBytes(content));

    public RelocationTestHarness WithSqliteDb(string relPath, bool corrupt = false)
    {
        var full = Path.Combine(CurrentDataDir, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using (var conn = new SqliteConnection($"Data Source={full}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE seed (id INTEGER); INSERT INTO seed VALUES (42);";
            cmd.ExecuteNonQuery();
        }

        if (corrupt)
        {
            // Truncate to simulate corruption.
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Write);
            fs.SetLength(8); // garbage header
        }

        return this;
    }

    public RelocationTestHarness WithMarker(PendingRelocation marker)
    {
        marker.SourceWhenCreated = CurrentDataDir;
        marker.WriteTo(CurrentDataDir);
        return this;
    }

    public RelocationTestHarness WithExistingTargetFile(string relPath, string content)
    {
        var full = Path.Combine(TargetDir, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return this;
    }

    public Task<RelocationOutcome> RunAsync(IProgress<RelocationProgress>? progress = null)
    {
        return PendingRelocationRunner.TryRunAsync(
            AnchorDir,
            () => PersistedDataPath ?? CurrentDataDir,
            (newPath, prevDataDir) =>
            {
                PersistedDataPath = newPath;
                if (prevDataDir != null) PersistedPrevDataPath = prevDataDir;
                return Task.CompletedTask;
            },
            progress);
    }

    public bool MarkerExistsInCurrent =>
        File.Exists(Path.Combine(CurrentDataDir, PendingRelocation.FileName));

    public bool MarkerExistsInTarget =>
        File.Exists(Path.Combine(TargetDir, PendingRelocation.FileName));

    public bool StagingExists =>
        Directory.Exists(Path.Combine(TargetDir, PendingRelocationRunner.StagingDirName));

    public byte[] ReadFromTarget(string relPath) =>
        File.ReadAllBytes(Path.Combine(TargetDir, relPath));

    public IEnumerable<string> EnumerateTargetFiles() =>
        Directory.Exists(TargetDir)
            ? Directory.EnumerateFiles(TargetDir, "*", SearchOption.AllDirectories)
            : System.Linq.Enumerable.Empty<string>();

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}
