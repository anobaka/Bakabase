using System;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.RemoteAccess;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.DataSync;

/// <summary>
/// Whether this install is headless (spec §2.11): what it tells other devices it is
/// (<see cref="IServerSelfDescription"/>), which follows <see cref="ServiceSelfDescription.KindOf"/> — the desktop
/// builds are desktop, Docker is headless, and otherwise a desktop exactly when it manages servers. Asked lazily: the
/// description resolves what is registered only when somebody wants it. It decides only whether this install creates
/// notifications (§9.4).
/// </summary>
public sealed class ServiceDataSyncHostKind(IServiceProvider services) : IDataSyncHostKind
{
    public bool IsHeadless =>
        (services.GetService<IServerSelfDescription>()?.Kind ?? ServiceSelfDescription.KindOf(AppService.RuntimeMode,
            services.GetService<IServiceProviderIsService>()?.IsService(typeof(IManagedServerService)) == true))
        == ServerKind.Headless;
}
