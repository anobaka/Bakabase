using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs;

public enum PythonRuntimeMode { Managed = 0, External = 1, Conda = 2 }

public record PythonRuntimeConfiguration
{
    public PythonRuntimeMode Mode { get; set; }
    /// <summary>Absolute path to an existing CPython executable on this server.</summary>
    public string? PythonExecutable { get; set; }
    /// <summary>Absolute path to the Conda executable on this server; shell scripts/batch commands are not accepted on Windows.</summary>
    public string? CondaExecutable { get; set; }
    /// <summary>Absolute path to the existing Conda environment on this server.</summary>
    public string? CondaPrefix { get; set; }
}

public record PythonRuntimeInfo(string PythonVersion, string Architecture, string Executable, string SourceFingerprint);

[Options]
public class PythonRuntimeOptions
{
    public PythonRuntimeConfiguration Configuration { get; set; } = new();
    public PythonRuntimeInfo? Runtime { get; set; }
}
