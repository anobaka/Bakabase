namespace Bakabase.Abstractions.Components.FileSystem;

/// <summary>
/// Ambient access point to <see cref="IAppDataPathRelocator"/> for use in static
/// extension methods that handle DB ↔ domain conversion. The host wires the live
/// relocator at startup; before then (and in unit tests that don't configure it),
/// reads/writes pass through unchanged.
/// </summary>
public static class AppDataPaths
{
    public static IAppDataPathRelocator Relocator { get; private set; } = PassthroughRelocator.Instance;

    /// <summary>Install the live relocator. Call once at app startup, after DI is built.</summary>
    public static void Configure(IAppDataPathRelocator relocator)
    {
        Relocator = relocator;
    }

    /// <summary>For tests: restore the no-op relocator.</summary>
    public static void Reset()
    {
        Relocator = PassthroughRelocator.Instance;
    }

    /// <summary>Convenience: resolve every path in the list. Pass-through on null.</summary>
    public static List<string>? ResolveAll(IEnumerable<string>? paths)
    {
        if (paths == null) return null;
        var r = Relocator;
        return paths.Select(p => r.Resolve(p)!).ToList();
    }

    /// <summary>Convenience: relativize every path in the list. Pass-through on null.</summary>
    public static List<string>? RelativizeAll(IEnumerable<string>? paths)
    {
        if (paths == null) return null;
        var r = Relocator;
        return paths.Select(p => r.Relativize(p)!).ToList();
    }

    private sealed class PassthroughRelocator : IAppDataPathRelocator
    {
        public static PassthroughRelocator Instance { get; } = new();
        public string? Resolve(string? stored) => stored;
        public string? Relativize(string? path) => path;
    }
}
