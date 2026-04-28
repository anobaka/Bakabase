using Bakabase.Modules.HealthScore.Components;

namespace Bakabase.Modules.HealthScore.Tests;

internal static class TestHelpers
{
    /// <summary>Creates a temporary directory that is deleted on dispose.</summary>
    public static TempDir CreateTempDir() => new();

    public sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "bakabase-healthscore-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Touch(string relativePath, byte[]? content = null)
        {
            var full = System.IO.Path.Combine(Path, relativePath);
            var dir = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(full, content ?? Array.Empty<byte>());
            return full;
        }

        public ResourceFsSnapshot Snapshot(IReadOnlyList<string>? coverPaths = null) =>
            new(Path, coverPaths);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
