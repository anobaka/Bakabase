using System.Text.Json;

namespace Bakabase.Abstractions.Components.FileSystem;

/// <summary>Previous appdata roots retained across imports, even after a version migrator clears PrevDataPath.</summary>
public static class ImportedAppDataRoots
{
    public const string FileName = "appdata-import-roots.json";

    public static string[] Read(string directory)
    {
        var file = Path.Combine(directory, FileName);
        return File.Exists(file) ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) ?? [] : [];
    }
}
