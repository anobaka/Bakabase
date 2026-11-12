using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Configs;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;

/// <summary>A captured source and launch context. Configuration switches never mutate an existing capture.</summary>
public sealed class PythonRuntimeExecution
{
    private readonly PythonRuntimeConfiguration _configuration;
    public PythonRuntimeConfiguration Configuration => _configuration with { };
    public PythonRuntimeInfo Info { get; }
    public string PythonExecutable { get; }

    public PythonRuntimeExecution(PythonRuntimeConfiguration configuration, PythonRuntimeInfo info, string? pythonExecutable = null)
    {
        _configuration = configuration with { };
        Info = info;
        PythonExecutable = pythonExecutable ?? info.Executable;
    }

    public PythonRuntimeExecution WithPython(string executable) => new(Configuration, Info, executable);

    public ProcessStartInfo StartInfo(IEnumerable<string> arguments, string directory)
    {
        // Reusing an interpreter must not create bytecode caches inside its system/Conda base.
        var pythonArguments = arguments.Prepend("-B").ToArray();
        if (Configuration.Mode != PythonRuntimeMode.Conda)
            return CloudToolProcess.StartInfo(PythonExecutable, pythonArguments, directory);
        var args = new List<string> { "run", "--no-capture-output", "--prefix", Configuration.CondaPrefix!, PythonExecutable };
        args.AddRange(pythonArguments);
        var start = CloudToolProcess.StartInfo(Configuration.CondaExecutable!, args, directory);
        start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        return start;
    }

    public Task<string> RunAsync(IEnumerable<string> arguments, string directory, CancellationToken ct,
        Action<ProcessStartInfo>? configure = null)
    {
        var start = StartInfo(arguments, directory);
        configure?.Invoke(start);
        return CloudToolProcess.RunAsync(start, ct);
    }
}
