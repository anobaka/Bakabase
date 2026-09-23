using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// The envelope the frontend expects, written from here rather than by the server.
/// </summary>
/// <remarks>
/// An intercepted route still has to answer in the shape its caller was written against,
/// or every handler becomes a special case in the UI. <c>code</c> is zero on success and
/// the status code otherwise, which is what the server's own responses do.
/// </remarks>
public static class UserMachineResponse
{
    public static async Task WriteAsync(HttpContext context, HttpStatusCode status, string? message)
    {
        context.Response.StatusCode = (int) status;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            code = status == HttpStatusCode.OK ? 0 : (int) status,
            message
        }), context.RequestAborted);
    }
}
