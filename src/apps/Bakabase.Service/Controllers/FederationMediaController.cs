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

    [HttpPost("playback-sessions")]
    [SwaggerOperation(OperationId = "CreateFederatedPlaybackSession")]
    [ProducesResponseType(typeof(PlaybackSessionResponse), 200)]
    public async Task<IActionResult> Prepare([FromBody] PlaybackSessionRequest request, CancellationToken ct) =>
        FederationResult(await media.PrepareAsync(request,
            $"{Request.Scheme}://127.0.0.1:{HttpContext.Connection.LocalPort}", ct));

    [HttpGet("media/{ticketId}")]
    [SwaggerOperation(OperationId = "ReadFederatedMediaSession")]
    public async Task Stream(string ticketId, CancellationToken ct)
    {
        var ticket = sessions.GetTicket(ticketId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct,
            ticket.Source.Peer == null ? CancellationToken.None :
                leases.GetCancellationToken("outbound:" + ticket.Source.Peer.GrantId));
        HttpContext.RequestAborted = linked.Token;
        var mappedPath = await media.ValidateAndMapAsync(ticket.Source, linked.Token);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; media-src 'self'; img-src 'self'";
        Response.Headers.CacheControl = "no-store";
        if (ticket.MappedPath != null)
        {
            if (mappedPath != ticket.MappedPath)
                throw new FederationQueryException("MappingChanged", 409, "Reopen playback after changing the path mapping.");
            await new PhysicalFileResult(mappedPath, ticket.Source.Asset.ContentType)
                { EnableRangeProcessing = true }.ExecuteResultAsync(ControllerContext);
            return;
        }
        var headers = new Dictionary<string, string>();
        foreach (var name in new[] { "Range", "If-Range" })
            if (Request.Headers.TryGetValue(name, out var value)) headers[name] = value.ToString();
        using var headerDeadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
        headerDeadline.CancelAfter(TimeSpan.FromSeconds(8));
        using var upstream = await transport.SendAsync(ticket.Source.Peer!,
            HttpMethods.IsHead(Request.Method) ? HttpMethod.Head : HttpMethod.Get,
            FederationMediaService.AssetPath(ticket.Source), cancellationToken: headerDeadline.Token, headers: headers);
        // Keep cancellation wired through the response lifetime, but do not impose a whole-film deadline.
        headerDeadline.CancelAfter(Timeout.InfiniteTimeSpan);
        if ((int)upstream.StatusCode is not (200 or 206 or 416)) FederationMediaService.EnsureSuccess(upstream);
        Response.StatusCode = (int)upstream.StatusCode;
        Response.ContentType = ticket.Source.Asset.ContentType;
        foreach (var name in new[] { "Content-Length", "Content-Range", "Accept-Ranges", "ETag", "Last-Modified" })
        {
            if (upstream.Content.Headers.TryGetValues(name, out var values) || upstream.Headers.TryGetValues(name, out values))
                Response.Headers[name] = string.Join(", ", values);
        }
        if (HttpMethods.IsHead(Request.Method)) return;
        await using var body = await upstream.Content.ReadAsStreamAsync(linked.Token);
        await FederationMediaStream.CopyAsync(body, Response.Body, TimeSpan.FromSeconds(30), linked.Token);
    }

    [HttpHead("media/{ticketId}")]
    [SwaggerOperation(OperationId = "InspectFederatedMediaSession")]
    public Task Inspect(string ticketId, CancellationToken ct) => Stream(ticketId, ct);
}
