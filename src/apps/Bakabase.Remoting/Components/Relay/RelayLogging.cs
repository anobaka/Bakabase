using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Remoting.Components.Relay;

/// <summary>
/// How loud a relay is in the log of the program that runs it: as loud as that program
/// says, except for the forwarder, which only speaks up when something is wrong.
/// </summary>
/// <remarks>
/// <para>
/// YARP writes two Information lines for every request it forwards — "Proxying to
/// http://&lt;server&gt;/&lt;path&gt;" and "Received HTTP/1.1 response 200." — and a relay
/// forwards every asset, API call and range request its window makes. In the desktop app
/// that was a pair of lines per request in the app's own persistent log, each naming the
/// managed server's address; the thin client's log filled the same way. Its warnings and
/// errors are kept: they are the forwarder's account of a failure, which is exactly what
/// the log is for. The relay's own lines — the guard's refusals, the tickets it takes off —
/// are not YARP's and pass through untouched.
/// </para>
/// <para>
/// A floor on the factory rather than a filter rule, because the desktop app's relays have
/// no logging configuration of their own to put a rule in: each logs through the app's own
/// factory (<see cref="Console.ManagedServerRelay"/>), whose rules come from the app's
/// container and configuration. A rule registered in a relay's container would be read by
/// nobody, and one added to the app's would reach past the relays. Wrapping whichever
/// factory the relay's container holds works the same for both products.
/// </para>
/// </remarks>
public static class RelayLogging
{
    /// <summary>The root of YARP's logging categories.</summary>
    public const string ForwarderCategory = "Yarp";

    /// <summary>The least a YARP category has to say to reach the log from a relay.</summary>
    public const LogLevel ForwarderMinimumLevel = LogLevel.Warning;

    /// <summary>Whether <paramref name="category"/> is one of YARP's.</summary>
    public static bool IsForwarderCategory(string category) =>
        category.StartsWith(ForwarderCategory, StringComparison.Ordinal) &&
        (category.Length == ForwarderCategory.Length || category[ForwarderCategory.Length] == '.');

    /// <summary>
    /// Replaces the container's logger factory with one that applies the forwarder's floor
    /// over it. Call after the composer has registered the factory the relay is to log
    /// through: the one registered last is the one wrapped.
    /// </summary>
    /// <remarks>
    /// Whoever owned the original keeps owning it. A factory handed over as an instance —
    /// the desktop app's own, which outlives every relay — is never disposed with the
    /// relay's container; one the container would have built itself is still built and
    /// disposed by it.
    /// </remarks>
    public static IServiceCollection AddRelayLoggingFloor(this IServiceCollection services)
    {
        // Whatever the composer registered; this only fills one in where there is none.
        services.AddLogging();

        var original = services.Last(d => d.ServiceType == typeof(ILoggerFactory) && !d.IsKeyedService);

        services.RemoveAll<ILoggerFactory>();

        Func<IServiceProvider, ILoggerFactory> inner;

        if (original.ImplementationInstance is ILoggerFactory instance)
        {
            inner = _ => instance;
        }
        else if (original.ImplementationFactory is { } factory)
        {
            // Resolved through a holder the container owns, so it is disposed as it would
            // have been had nobody wrapped it.
            services.Add(new ServiceDescriptor(typeof(OwnedLoggerFactory),
                sp => new OwnedLoggerFactory((ILoggerFactory) factory(sp)), original.Lifetime));
            inner = sp => sp.GetRequiredService<OwnedLoggerFactory>().Factory;
        }
        else
        {
            var type = original.ImplementationType!;

            services.TryAdd(new ServiceDescriptor(type, type, original.Lifetime));
            inner = sp => (ILoggerFactory) sp.GetRequiredService(type);
        }

        services.Add(new ServiceDescriptor(typeof(ILoggerFactory),
            sp => new FlooredLoggerFactory(inner(sp)), original.Lifetime));

        return services;
    }

    /// <summary>A factory the container built, held so the container disposes it.</summary>
    private sealed class OwnedLoggerFactory(ILoggerFactory factory) : IDisposable
    {
        public ILoggerFactory Factory { get; } = factory;

        public void Dispose() => Factory.Dispose();
    }

    /// <summary>
    /// The factory a relay's services log through. Disposing it disposes nothing: the
    /// factory underneath belongs to whoever registered it.
    /// </summary>
    internal sealed class FlooredLoggerFactory(ILoggerFactory inner) : ILoggerFactory
    {
        public ILoggerFactory Inner { get; } = inner;

        public ILogger CreateLogger(string categoryName)
        {
            var logger = Inner.CreateLogger(categoryName);

            return IsForwarderCategory(categoryName) ? new FlooredLogger(logger) : logger;
        }

        public void AddProvider(ILoggerProvider provider) => Inner.AddProvider(provider);

        public void Dispose()
        {
        }
    }

    private sealed class FlooredLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel >= ForwarderMinimumLevel && inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= ForwarderMinimumLevel)
            {
                inner.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
