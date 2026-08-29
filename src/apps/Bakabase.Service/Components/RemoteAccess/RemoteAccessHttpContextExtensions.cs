using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Service.Components.RemoteAccess
{
    public static class RemoteAccessHttpContextExtensions
    {
        private const string ContextKey = "Bakabase.RemoteAccess.Context";

        public static void SetRemoteAccessContext(this HttpContext httpContext, RemoteAccessContext context) =>
            httpContext.Items[ContextKey] = context;

        /// <summary>
        /// The decision the middleware reached for this request. Null only if the
        /// middleware did not run, which the filters treat as "deny".
        /// </summary>
        public static RemoteAccessContext? GetRemoteAccessContext(this HttpContext httpContext) =>
            httpContext.Items.TryGetValue(ContextKey, out var value) ? value as RemoteAccessContext : null;
    }
}
