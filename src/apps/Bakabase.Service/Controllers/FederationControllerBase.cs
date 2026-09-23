using System.Text.Json;
using Bakabase.Modules.Federation;
using Microsoft.AspNetCore.Mvc;

namespace Bakabase.Service.Controllers;

public abstract class FederationControllerBase : ControllerBase
{
    protected ContentResult FederationResult<T>(T value, int status = 200)
    {
        // Some existing local storage APIs cannot consume a cancellation token.
        // Their late result must not become a successful response after revocation.
        HttpContext?.RequestAborted.ThrowIfCancellationRequested();
        var content = JsonSerializer.Serialize(value, FederationJson.Options);
        HttpContext?.RequestAborted.ThrowIfCancellationRequested();
        return new ContentResult
        {
            Content = content,
            ContentType = "application/json; charset=utf-8",
            StatusCode = status
        };
    }
}
