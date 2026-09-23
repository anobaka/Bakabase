using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Service.Components.Federation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("federation/local")]
[FederationEndpoint(FederationEndpointKind.Local)]
public sealed class FederationMediaController(FederationMediaService media, FederationMediaSessions sessions,
    INodeTransport transport, GrantLeaseRegistry leases) : FederationControllerBase
{
    [HttpPost("resources/resolve")]
    [SwaggerOperation(OperationId = "ResolveFederatedResources")]
    [ProducesResponseType(typeof(ResourceResolveResponse), 200)]
    public async Task<IActionResult> Resolve([FromBody] ResourceResolveRequest request, CancellationToken ct) =>
        FederationResult(await media.ResolveAsync(request, ct));

    [HttpPost("resources/open-directory")]
    [SwaggerOperation(OperationId = "OpenFederatedResourceDirectory")]
    [ProducesResponseType(typeof(OpenResourceDirectoryResponse), 200)]
    public async Task<IActionResult> OpenDirectory([FromBody] OpenResourceDirectoryRequest request,
        [FromServices] FederationDirectoryService directories, CancellationToken ct) =>
        FederationResult(await directories.OpenAsync(request.ResourceRef, ct));

    [HttpPost("playback-sessions")]
    [SwaggerOperation(OperationId = "CreateFederatedPlaybackSession")]
    [ProducesResponseType(typeof(PlaybackSessionResponse), 200)]
    public async Task<IActionResult> Prepare([FromBody] PlaybackSessionRequest request, CancellationToken ct) =>
        FederationResult(await media.PrepareAsync(request,
            // The local gate has already validated this loopback Host and port.
            // Preserve localhost/IPv6 spelling so browser previews remain same-origin.
            $"{Request.Scheme}://{Request.Host}", ct));

    [HttpGet("media/{ticketId}")]
    [SwaggerOperation(OperationId = "ReadFederatedMediaSession")]
    public async Task Stream(string ticketId, CancellationToken ct)
    {
        var ticket = sessions.GetTicket(ticketId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct,
            ticket.Source.Peer == null ? CancellationToken.None :
                leases.GetCancellationToken("outbound:" + ticket.Source.Peer.GrantId));
        HttpContext.RequestAborted = linked.Token;
        await media.RenewAsync(ticket, false, linked.Token);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; media-src 'self'; img-src 'self'";
        Response.Headers.CacheControl = "no-store";
        if (ticket.MappedPath != null)
        {
            // A local copy is served only after the source validates the current grant and asset.
            string? mappedPath;
            try { mappedPath = await media.ValidateAndMapAsync(ticket.Source, linked.Token); }
            catch (FederationQueryException e) when (e.Code == "AssetExpired")
            {
                // The source may have restarted or evicted the lease early; one authenticated renewal.
                await media.RenewAsync(ticket, true, linked.Token);
                mappedPath = await media.ValidateAndMapAsync(ticket.Source, linked.Token);
            }
            if (mappedPath != ticket.MappedPath)
                throw new FederationQueryException("MappingChanged", 409, "Reopen playback after changing the path mapping.");
            await new PhysicalFileResult(mappedPath, ticket.Source.Asset.ContentType)
                { EnableRangeProcessing = true }.ExecuteResultAsync(ControllerContext);
            return;
        }
        // The source authorizes the proxied read itself, so no separate validation round trip.
        var source = ticket.Source;
        HttpResponseMessage upstream;
        try { upstream = await SendUpstreamAsync(source, linked.Token); }
        catch (FederationAccessException e) when (e.ErrorCode == "NodeSessionChanged")
        {
            // The peer moved or re-verified since this ticket was issued; resolve with the current session.
            await media.RenewAsync(ticket, true, linked.Token);
            source = ticket.Source;
            upstream = await SendUpstreamAsync(source, linked.Token);
        }
        try
        {
            if (upstream.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                upstream.Dispose();
                await media.RenewAsync(ticket, true, linked.Token);
                source = ticket.Source;
                upstream = await SendUpstreamAsync(source, linked.Token);
            }
            if ((int)upstream.StatusCode is not (200 or 206 or 416)) FederationMediaService.EnsureSuccess(upstream);
            Response.StatusCode = (int)upstream.StatusCode;
            Response.ContentType = source.Asset.ContentType;
            foreach (var name in new[] { "Content-Length", "Content-Range", "Accept-Ranges", "ETag", "Last-Modified" })
            {
                if (upstream.Content.Headers.TryGetValues(name, out var values) || upstream.Headers.TryGetValues(name, out values))
                    Response.Headers[name] = string.Join(", ", values);
            }
            if (HttpMethods.IsHead(Request.Method)) return;
            await using var body = await upstream.Content.ReadAsStreamAsync(linked.Token);
            await FederationMediaStream.CopyAsync(body, Response.Body, TimeSpan.FromSeconds(30), linked.Token);
        }
        finally { upstream.Dispose(); }
    }

    private async Task<HttpResponseMessage> SendUpstreamAsync(AvailableFederationAsset source, CancellationToken ct)
    {
        var headers = new Dictionary<string, string>();
        foreach (var name in new[] { "Range", "If-Range" })
            if (Request.Headers.TryGetValue(name, out var value)) headers[name] = value.ToString();
        using var headerDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headerDeadline.CancelAfter(TimeSpan.FromSeconds(8));
        // Only the response headers are bounded; an active long stream is limited by read idleness.
        return await transport.SendAsync(source.Peer!,
            HttpMethods.IsHead(Request.Method) ? HttpMethod.Head : HttpMethod.Get,
            FederationMediaService.AssetPath(source), cancellationToken: headerDeadline.Token, headers: headers);
    }

    [HttpHead("media/{ticketId}")]
    [SwaggerOperation(OperationId = "InspectFederatedMediaSession")]
    public Task Inspect(string ticketId, CancellationToken ct) => Stream(ticketId, ct);
}
