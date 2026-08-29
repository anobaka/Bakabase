using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.RemoteAccess.Components.Discovery;

/// <summary>
/// Keeps the discovery beacon in step with the remote-access mode: as long as
/// remote access is on, this server is discoverable (mDNS + UDP probe); the
/// moment it is off, nothing on the network can even tell Bakabase is here.
/// <para>
/// Polls rather than subscribing to option changes — the state is two booleans
/// and a port, a few seconds of latency on toggle is invisible, and polling
/// also covers the startup race where Kestrel has not reported its port yet.
/// </para>
/// </summary>
public class RemoteAccessDiscoveryService(
    IRemoteAccessService remoteAccessService,
    ILogger<RemoteAccessDiscoveryService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private MdnsResponder? _mdns;
    private UdpProbeResponder? _probe;
    private int? _advertisedPort;
    private bool _lastTickFailed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(stoppingToken);
                    _lastTickFailed = false;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    // Log the first failure at warning, repeats at debug: a broken
                    // network stack would otherwise fill the log every 5 seconds.
                    if (!_lastTickFailed)
                    {
                        logger.LogWarning(e, "Discovery beacon tick failed; will keep retrying quietly");
                        _lastTickFailed = true;
                    }
                    else
                    {
                        logger.LogDebug(e, "Discovery beacon tick failed again");
                    }
                }

                await Task.Delay(PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            StopResponders();
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var mode = remoteAccessService.GetEffectiveMode();

        if (mode == RemoteAccessMode.Disabled)
        {
            if (_advertisedPort != null)
            {
                logger.LogInformation("Remote access disabled; discovery beacon stopping");
                StopResponders();
            }

            return;
        }

        // Only reached when remote access is on, so a desktop install with the
        // default (Disabled) mode never writes a server id to disk.
        var descriptor = await remoteAccessService.GetServerDescriptorAsync();

        if (descriptor.Port == null)
        {
            // Kestrel has not reported its addresses yet; try again next tick.
            return;
        }

        if (_advertisedPort == descriptor.Port)
        {
            return;
        }

        StopResponders();

        var advertisement = new MdnsAdvertisement(descriptor);
        var mdns = new MdnsResponder(advertisement,
            () => LocalNetworkAddresses.EnumerateIPv4(logger).Select(a => a.Address).ToArray(),
            logger);
        var probe = new UdpProbeResponder(descriptor, logger);

        var mdnsUp = mdns.Start();
        var probeUp = probe.Start();

        if (!mdnsUp && !probeUp)
        {
            mdns.Dispose();
            probe.Dispose();
            throw new InvalidOperationException("Neither discovery channel could start");
        }

        _mdns = mdnsUp ? mdns : null;
        if (!mdnsUp)
        {
            mdns.Dispose();
        }

        _probe = probeUp ? probe : null;
        if (!probeUp)
        {
            probe.Dispose();
        }

        _advertisedPort = descriptor.Port;
        logger.LogInformation(
            "Discovery beacon up for {Instance} on port {Port} (mDNS: {Mdns}, UDP probe: {Probe})",
            advertisement.InstanceName, descriptor.Port, mdnsUp, probeUp);

        if (_mdns != null)
        {
            await _mdns.AnnounceAsync(ct);
        }
    }

    private void StopResponders()
    {
        try
        {
            _mdns?.SayGoodbye();
        }
        catch
        {
            // Goodbye is best-effort; the records expire by TTL anyway.
        }

        _mdns?.Dispose();
        _mdns = null;
        _probe?.Dispose();
        _probe = null;
        _advertisedPort = null;
    }
}
