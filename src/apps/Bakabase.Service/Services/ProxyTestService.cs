using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Service.Models.View;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Services;

/// <summary>
/// Runs connectivity checks for a proxy against a set of destinations.
/// </summary>
/// <remarks>
/// Deliberately does not go through the app-wide <c>BakabaseWebProxy</c>: the point is to
/// try a proxy the user has not committed to yet (or to compare against no proxy at all),
/// so each run builds its own handler.
/// </remarks>
public class ProxyTestService(ILogger<ProxyTestService> logger)
{
    /// <summary>
    /// Per-site ceiling. A dead proxy typically hangs rather than refusing, so without this
    /// the whole test sits at the framework default (100s) per site.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public async Task<List<ProxyTestResultViewModel>> TestAsync(
        string? proxyAddress,
        NetworkOptions.ProxyOptions.ProxyCredentials? credentials,
        IReadOnlyCollection<ProxyTestTarget> targets,
        bool useSystemProxy = false,
        CancellationToken ct = default)
    {
        if (targets.Count == 0)
        {
            return [];
        }

        using var handler = BuildHandler(proxyAddress, credentials, useSystemProxy);
        using var client = new HttpClient(handler) {Timeout = RequestTimeout};

        // Sites are independent, and a serial run would take the sum of every timeout.
        var tasks = targets.Select(target => TestOneAsync(client, target, ct));
        var results = await Task.WhenAll(tasks);

        return results.ToList();
    }

    private static HttpClientHandler BuildHandler(
        string? proxyAddress,
        NetworkOptions.ProxyOptions.ProxyCredentials? credentials,
        bool useSystemProxy)
    {
        var handler = new HttpClientHandler {AllowAutoRedirect = true};

        if (string.IsNullOrWhiteSpace(proxyAddress))
        {
            // Leaving Proxy unset with UseProxy=true is what picks up the system setting;
            // UseProxy=false is an explicit direct connection that bypasses it, so the user
            // can compare the two and tell a broken proxy from a broken network.
            handler.UseProxy = useSystemProxy;

            return handler;
        }

        var proxy = new WebProxy(proxyAddress);

        if (credentials != null)
        {
            proxy.Credentials = new NetworkCredential(credentials.Username, credentials.Password,
                credentials.Domain);
        }

        handler.Proxy = proxy;
        handler.UseProxy = true;

        return handler;
    }

    private async Task<ProxyTestResultViewModel> TestOneAsync(HttpClient client, ProxyTestTarget target,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // HEAD would be cheaper but several of these destinations reject it, so use GET
            // and stop as soon as the headers land rather than downloading the body.
            using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
            using var response =
                await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            sw.Stop();

            return new ProxyTestResultViewModel
            {
                Id = target.Id,
                Name = target.Name,
                Url = target.Url,
                // Any answer at all proves the tunnel carried the request; a 403 from the
                // destination still means the proxy reached it.
                Succeeded = true,
                StatusCode = (int) response.StatusCode,
                ElapsedMs = (int) sw.ElapsedMilliseconds,
            };
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            sw.Stop();
            logger.LogDebug(e, "Proxy test failed for {Url}", target.Url);

            return new ProxyTestResultViewModel
            {
                Id = target.Id,
                Name = target.Name,
                Url = target.Url,
                Succeeded = false,
                ElapsedMs = (int) sw.ElapsedMilliseconds,
                // A timeout surfaces as a cancellation with no useful message, so name it.
                Error = e is TaskCanceledException or TimeoutException
                    ? $"Timed out after {RequestTimeout.TotalSeconds:0}s"
                    : BuildErrorMessage(e),
            };
        }
    }

    /// <summary>
    /// Flattens the exception chain. .NET puts the useful part of a TLS or DNS failure in an
    /// inner exception, so the outer message alone is usually just "An error occurred".
    /// </summary>
    private static string BuildErrorMessage(Exception e)
    {
        var messages = new List<string>();
        var current = (Exception?) e;

        while (current != null && messages.Count < 4)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) && !messages.Contains(current.Message))
            {
                messages.Add(current.Message);
            }

            current = current.InnerException;
        }

        return string.Join(" → ", messages);
    }
}

public record ProxyTestTarget(string Id, string Name, string Url);
