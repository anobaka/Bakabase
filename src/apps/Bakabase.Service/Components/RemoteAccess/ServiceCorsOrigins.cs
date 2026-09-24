using System.Collections.Generic;
using System.Linq;
using Bakabase.Infrastructures.Components.App;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Bakabase.Service.Components.RemoteAccess;

/// <summary>
/// The pages this Service lets a browser call it from: the allow-list its CORS policy is
/// built from, kept in one place so <see cref="LoopbackCrossSiteGuard"/> asks the same
/// question the CORS middleware does.
/// </summary>
/// <remarks>
/// <para>
/// The policy itself is composed by <c>AppStartup.Configure</c> through
/// <see cref="CorsExtensions.UseBootstrapCors"/>: the dev server's origin
/// (<see cref="CorsExtensions.WithDevOrigins"/>), this Service's own endpoints, and
/// whatever <c>BakabaseStartup.ConfigureCors</c> does on top — which is
/// <see cref="Configure"/>. <see cref="Build"/> repeats that composition from the same
/// three sources rather than restating any of it, so an origin added to CORS is trusted
/// by the guard without anyone having to remember the guard exists.
/// </para>
/// <para>
/// <c>yarn dev</c>'s server is trusted only by a development build, the same line
/// <c>FederationAccessMiddleware</c> draws. A packaged app never talks to a dev server,
/// and trusting <c>localhost:3000</c> there would let whatever happens to serve pages on
/// that port — any web project someone is working on — call this Service, change things
/// and read the answers. The runtime is a constructor argument rather than a read of
/// <see cref="AppService.RuntimeMode"/> so both answers can be tested from one build;
/// <see cref="ForThisBuild"/> is the one the Service runs with.
/// </para>
/// </remarks>
public sealed class ServiceCorsOrigins(RuntimeMode runtimeMode)
{
    /// <summary>
    /// Sites the Bakabase userscript runs on. It calls this Service from their pages, to
    /// mark what is already downloaded and to queue new downloads.
    /// </summary>
    public static readonly IReadOnlyList<string> UserscriptSites =
    [
        "https://www.north-plus.net",
        "https://exhentai.org"
    ];

    /// <summary>
    /// <c>yarn dev</c>'s server, exactly as <see cref="CorsExtensions.WithDevOrigins"/> adds
    /// it to a policy — read back from there rather than restated, so the two cannot drift.
    /// </summary>
    public static readonly IReadOnlyList<string> DevServerOrigins =
        new CorsPolicyBuilder().WithDevOrigins().Build().Origins.ToArray();

    /// <summary>What the Service runs with: decided by the runtime this build was compiled for.</summary>
    public static ServiceCorsOrigins ForThisBuild { get; } = new(AppService.RuntimeMode);

    /// <summary>Whether <c>yarn dev</c>'s server is one of the trusted pages.</summary>
    public bool TrustsDevServer { get; } = runtimeMode == RuntimeMode.Dev;

    /// <summary>What <c>BakabaseStartup.ConfigureCors</c> does to the base policy.</summary>
    public void Configure(CorsPolicyBuilder builder)
    {
        builder.WithOrigins(UserscriptSites.ToArray());

        if (!TrustsDevServer)
        {
            // UseBootstrapCors has already added the dev server, and WithDevOrigins has no
            // inverse. Build() hands back the policy under construction, not a copy, so
            // the origins come off the policy the middleware is about to be created with.
            // The gate tests hold a release build's preflight to that.
            var origins = builder.Build().Origins;
            foreach (var origin in DevServerOrigins)
            {
                origins.Remove(origin);
            }
        }
    }

    /// <summary>The origin half of the policy the CORS middleware runs with.</summary>
    /// <param name="apiEndpoints">This Service's own endpoints, as <c>AppContext.ApiEndpoints</c> lists them.</param>
    public CorsPolicy Build(IEnumerable<string>? apiEndpoints)
    {
        var builder = new CorsPolicyBuilder().WithDevOrigins().WithOrigins((apiEndpoints ?? []).ToArray());
        Configure(builder);
        return builder.Build();
    }

    /// <summary>
    /// The pages allowed to put this Service's pages in a frame, as a
    /// <c>frame-ancestors</c> source list: its own, and the dev server's where that is
    /// trusted — <c>yarn dev</c>'s profiler page frames the API's.
    /// </summary>
    public string FrameAncestors =>
        TrustsDevServer ? string.Join(' ', new[] {"'self'"}.Concat(DevServerOrigins)) : "'self'";
}
