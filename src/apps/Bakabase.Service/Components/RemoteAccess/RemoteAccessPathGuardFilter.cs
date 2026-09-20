using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Bakabase.Service.Components.RemoteAccess
{
    /// <summary>
    /// Keeps a remote caller inside the user's libraries.
    /// <para>
    /// Bakabase's file endpoints take a path and read it off the host's disk, which is
    /// fine while only the host's own browser can call them. For a remote caller the
    /// path is checked against <see cref="IMediaPathGuard"/> first, so
    /// <c>/file/play?fullname=C:/Users/…/passwords.txt</c> is refused even though the
    /// server process could read it.
    /// </para>
    /// <para>
    /// Loopback callers are deliberately not checked: the desktop UI legitimately
    /// browses and previews files anywhere on disk (the file explorer, the file
    /// processor, cover pickers), and constraining it would break those.
    /// </para>
    /// </summary>
    public class RemoteAccessPathGuardFilter(IMediaPathGuard pathGuard) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // The separate node boundary has already authenticated this explicit protocol action.
            if (Bakabase.Service.Components.Federation.FederationLocalAccessFilter.HasHandledEndpoint(context))
            {
                await next();
                return;
            }

            var remoteContext = context.HttpContext.GetRemoteAccessContext();

            // A paired device is one the operator deliberately let in, and the point of
            // pairing is parity with sitting at the machine — a playlist entry, a
            // subtitle or a cover may legitimately live outside every library root. The
            // guard exists for callers nobody vouched for.
            if (remoteContext is {IsUnrestricted: true} or {IsPaired: true})
            {
                await next();
                return;
            }

            var attribute = RemoteAccessAuthorizationFilter.FindAttribute(context);
            if (attribute is {PathParameters.Length: > 0})
            {
                foreach (var parameterName in attribute.PathParameters)
                {
                    // A parameter declared as a guarded path must actually arrive as
                    // one. Anything else — missing, null, bound to another type after a
                    // signature change — is refused rather than waved through, so a
                    // rename cannot silently disable the guard.
                    var bound = context.ActionArguments.TryGetValue(parameterName, out var value);

                    if (!bound || value is not string path || string.IsNullOrWhiteSpace(path) ||
                        !await pathGuard.IsServableAsync(path, context.HttpContext.RequestAborted))
                    {
                        context.HttpContext.Response.Headers["X-Bakabase-Remote-Access"] =
                            RemoteAccessDenialReason.PathNotServable.ToString();
                        context.Result = new ObjectResult(BaseResponseBuilder.Build(ResponseCode.Unauthorized,
                            "This file is outside the media libraries Bakabase serves."))
                        {
                            StatusCode = (int) HttpStatusCode.Forbidden
                        };
                        return;
                    }
                }
            }

            await next();
        }
    }
}
