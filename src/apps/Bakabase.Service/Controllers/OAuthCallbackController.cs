using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.OAuth;
using Bakabase.Service.Components.RemoteAccess;
using Microsoft.AspNetCore.Mvc;

namespace Bakabase.Service.Controllers;

/// <summary>Public protocol completion only; owner configuration/status remain on the normal administration boundary.</summary>
public sealed class OAuthCallbackController(OAuthAuthorizationBroker broker) : Controller
{
    [HttpGet("~/oauth/callback/{providerId}")]
    [RemoteAccessible]
    public async Task<ContentResult> Complete(string providerId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
        var completed = false;
        try
        {
            if (OAuthProviderRegistry.IsCallbackRequest(Request))
                completed = await broker.CompleteAsync(providerId, Request.Path.Value ?? "", Request.Query, ct);
        }
        catch (Exception) { /* Never pass a callback query or external error into exception/request logging. */ }
        const string success = "授权结果已收到。请返回原来的 Bakabase 标签页，继续选择站点或文档库。<br>Authorization received. Return to the original Bakabase tab to continue selecting the site or document library.";
        const string failure = "未能完成授权。请返回原来的 Bakabase 标签页查看状态并重试。<br>Authorization could not be completed. Return to the original Bakabase tab to check its status and retry.<br>OAUTH_CALLBACK_FAILED";
        return new ContentResult {StatusCode = completed ? 200 : 400, ContentType = "text/html; charset=utf-8",
            Content = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Bakabase authorization</title></head><body><p>" +
                      (completed ? success : failure) + "</p></body></html>"};
    }
}
