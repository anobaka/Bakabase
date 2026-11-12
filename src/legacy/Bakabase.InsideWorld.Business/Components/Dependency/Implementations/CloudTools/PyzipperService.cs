using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;

/// <summary>Optional isolated ZIP compatibility engine; shares the managed/external/Conda Python source.</summary>
public sealed class PyzipperService(ILoggerFactory loggerFactory, AppService appService, CloudPythonRuntimeService python,
    BakabaseWebProxy proxy, IServiceProvider services) : CloudPythonToolService(loggerFactory, appService, "pyzipper", python, proxy, services)
{
    public const string ComponentId = "pyzipper-component-service";
    public override string Id => ComponentId;
    protected override string KeyInLocalizer => "Pyzipper";
    protected override string PackageName => "pyzipper";
    public override string ValidatedVersion => "0.4.0";
    public override Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct) =>
        Task.FromResult(new DependentComponentVersion {Version = ValidatedVersion,
            Description = "pyzipper 0.4.0; official release and authenticated ZIP reading verified 2026-11-04. Explicit password encoding or structurally verified ZIP/ZIP64 media wrappers."});
}
