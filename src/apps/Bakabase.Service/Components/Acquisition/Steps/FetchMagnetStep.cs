using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Steps;

/// <summary>
/// Hands a magnet link to something that can fetch it.
/// <para>
/// Bakabase does not speak BitTorrent and should not learn to: a torrent client is a long-running
/// networked thing with its own settings, ports and etiquette, and everyone who uses magnets
/// already has one. This step is the handover.
/// </para>
/// </summary>
public class FetchMagnetStep : IAcquisitionStep
{
    public string Kind => AcquisitionStepKinds.FetchMagnet;
    public string DisplayName => "Fetch the magnet link";
    public Type? ConfigType => typeof(Config);

    /// <summary>Where the magnet is sent.</summary>
    public enum Handler
    {
        /// <summary>
        /// An aria2 daemon over JSON-RPC. The run watches it and carries on by itself when the
        /// files land, so a chain that ends in a magnet finishes unattended.
        /// </summary>
        Aria2 = 1,

        /// <summary>
        /// Whatever the operating system opens magnet links with. The files then arrive wherever
        /// that client puts them, so a <c>waitForInbox</c> step belongs after this one.
        /// </summary>
        SystemDefault = 2
    }

    public record Config
    {
        public Handler Handler { get; init; } = Handler.Aria2;

        /// <summary>aria2's RPC endpoint. The default is what <c>aria2c --enable-rpc</c> listens on.</summary>
        public string RpcUrl { get; init; } = "http://127.0.0.1:6800/jsonrpc";

        /// <summary>The token from <c>--rpc-secret</c>, when the daemon was started with one.</summary>
        public string? Secret { get; init; }

        /// <summary>
        /// How long to keep watching before giving up. A torrent with no seeds never fails, it
        /// simply never finishes, so something has to decide when that has gone on long enough.
        /// </summary>
        public int TimeoutMinutes { get; init; } = 240;

        /// <summary>How often to ask aria2 how it is going.</summary>
        public int PollSeconds { get; init; } = 5;
    }

    public async Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, CancellationToken ct)
    {
        var link = item.SelectedLink ?? item.Links.FirstOrDefault();

        if (link == null) return new AcquisitionStepOutcome.Fail("There is no link to fetch.");

        if (link.DriveKind != AcquisitionDriveKind.Magnet)
        {
            // Not a failure: a recipe puts this alongside the other fetch steps and lets the link
            // decide which of them applies.
            return new AcquisitionStepOutcome.Skip($"A {link.DriveKind} link is not a magnet.", item);
        }

        var cfg = ctx.GetConfig<Config>() ?? new Config();

        Directory.CreateDirectory(ctx.WorkingDirectory);

        return cfg.Handler == Handler.SystemDefault
            ? HandToTheSystem(ctx, item, link.Url)
            : await FetchWithAria2(ctx, item, link.Url, cfg, ct);
    }

    /// <summary>
    /// Opens the magnet with whatever the machine uses for them. Nothing is fetched here — the
    /// client puts the files where it is configured to, which is why this pairs with the inbox.
    /// </summary>
    private static AcquisitionStepOutcome HandToTheSystem(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, string magnet)
    {
        try
        {
            Process.Start(new ProcessStartInfo(magnet) {UseShellExecute = true});

            ctx.Logger.LogInformation(
                "Handed the magnet to this machine's torrent client. The files arrive through the inbox.");

            return new AcquisitionStepOutcome.Continue(item);
        }
        catch (Exception ex)
        {
            return new AcquisitionStepOutcome.Fail(
                $"Nothing on this machine opens magnet links: {ex.Message}", ex);
        }
    }

    private static async Task<AcquisitionStepOutcome> FetchWithAria2(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, string magnet, Config cfg, CancellationToken ct)
    {
        var http = ctx.ServiceProvider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(FetchMagnetStep));

        string gid;
        try
        {
            gid = Aria2Rpc.ReadGid(await Call(http, cfg.RpcUrl,
                Aria2Rpc.BuildAddUri(magnet, ctx.WorkingDirectory, cfg.Secret, "add"), ct));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AcquisitionStepOutcome.Fail(
                $"Could not reach aria2 at {cfg.RpcUrl}: {ex.Message}", ex);
        }

        ctx.Logger.LogInformation("aria2 took the magnet as {Gid}", gid);

        var deadline = DateTime.UtcNow.AddMinutes(Math.Max(1, cfg.TimeoutMinutes));
        var poll = TimeSpan.FromSeconds(Math.Clamp(cfg.PollSeconds, 1, 60));

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(poll, ct);

            Aria2Status status;
            try
            {
                status = Aria2Rpc.ReadStatus(await Call(http, cfg.RpcUrl,
                    Aria2Rpc.BuildTellStatus(gid, cfg.Secret, "status"), ct));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new AcquisitionStepOutcome.Fail($"aria2 stopped answering: {ex.Message}", ex);
            }

            if (status.IsFailed)
            {
                return new AcquisitionStepOutcome.Fail(
                    $"aria2 gave up on the magnet: {status.ErrorMessage ?? status.Status}");
            }

            await ctx.ReportProgress(status.Percentage, "Fetching the torrent");

            if (!status.IsComplete) continue;

            // A magnet completes twice: first the metadata, then the files it described. Following
            // the second id is what separates "aria2 finished" from "the files are here".
            if (status.FollowedBy is { } next)
            {
                ctx.Logger.LogInformation("The metadata resolved; the files are coming as {Gid}", next);
                gid = next;

                continue;
            }

            var files = status.Files.Where(File.Exists).Distinct().ToList();

            if (files.Count == 0)
            {
                return new AcquisitionStepOutcome.Fail(
                    "aria2 reported the magnet complete but wrote no files this run can see.");
            }

            return new AcquisitionStepOutcome.Continue(item with
            {
                // Distinct: the host re-runs the step at the cursor after a restart, and the same
                // files must not be listed twice.
                Files = item.Files.Concat(files).Distinct().ToList()
            });
        }

        return new AcquisitionStepOutcome.Fail(
            $"The magnet was still not finished after {cfg.TimeoutMinutes} minutes.");
    }

    private static async Task<string> Call(HttpClient http, string rpcUrl, string body,
        CancellationToken ct)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(rpcUrl, content, ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(ct);
    }
}
