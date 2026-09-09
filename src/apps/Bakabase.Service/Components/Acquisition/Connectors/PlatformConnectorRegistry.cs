using System;
using System.Collections.Generic;
using Bakabase.Abstractions.Components.Platform;
using Bakabase.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Acquisition.Connectors;

/// <summary>
/// The platforms this build speaks for, resolved one at a time.
/// <para>
/// Keyed by source rather than enumerated: a connector carries a client, a session or a download
/// queue with it, and building all three to use one is work nobody asked for.
/// </para>
/// </summary>
public class PlatformConnectorRegistry(IServiceProvider serviceProvider) : IPlatformConnectorRegistry
{
    /// <summary>
    /// Registered here rather than discovered, because discovery would mean constructing each
    /// connector to ask it which platform it is — the very thing this exists to avoid.
    /// </summary>
    public IReadOnlyCollection<ResourceSource> Sources { get; } =
    [
        ResourceSource.DLsite,
        ResourceSource.Steam,
        ResourceSource.ExHentai,
    ];

    public IPlatformConnector? Get(ResourceSource source) =>
        serviceProvider.GetKeyedService<IPlatformConnector>(source);
}
