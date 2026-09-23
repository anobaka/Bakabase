using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Service.Components.Federation;

/// <summary>Closing remote access also closes existing export streams and snapshots.</summary>
public sealed class FederationRemoteModeMonitor(IBOptionsManager<RemoteAccessOptions> options,
    RemoteAccessDefaults defaults, GrantLeaseRegistry leases) : IHostedService, IDisposable
{
    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken ct)
    {
        if (options is AspNetCoreOptionsManager<RemoteAccessOptions> manager)
            _subscription = manager.OnChange(value =>
            {
                if ((value.Mode ?? defaults.Mode) == RemoteAccessMode.Disabled) leases.CancelInbound();
                else leases.Resume();
            });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) { Dispose(); return Task.CompletedTask; }
    public void Dispose() { _subscription?.Dispose(); _subscription = null; }
}
