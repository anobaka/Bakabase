using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.RemoteAccess;

/// <summary>
/// Tells the browser that only this Service's own pages may show its pages in a frame.
/// </summary>
/// <remarks>
/// <para>
/// A page on another site that frames this device's UI — invisibly, over a button of its
/// own — gets the user's clicks without ever sending a request of its own: every request
/// the framed UI makes is same-origin, so neither <see cref="LoopbackCrossSiteGuard"/> nor
/// the CORS policy ever sees a foreign page. The guard already refuses the frame's own
/// load when the browser labels it with fetch metadata; this is the same rule for the
/// engines that do not, enforced by the browser itself.
/// </para>
/// <para>
/// Sent on every response rather than only on HTML: a browser consults these headers only
/// for a document it is asked to put in a frame, so they cost nothing anywhere else, and
/// the rule then does not depend on every endpoint labelling its content type right.
/// </para>
/// <para>
/// <c>frame-ancestors</c> is what current browsers enforce, and it wins over
/// <c>X-Frame-Options</c> wherever both are sent. The older header is for the engines
/// that know only it, and is left off when the list allows more than this origin, which
/// it cannot express — in a development build, where <c>yarn dev</c>'s profiler page
/// frames the API's. Both are added next to whatever an endpoint set itself: another
/// <c>Content-Security-Policy</c> is enforced alongside, not instead.
/// </para>
/// </remarks>
public sealed class FrameAncestorsPolicy(RequestDelegate next)
{
    public const string ContentSecurityPolicyHeader = "Content-Security-Policy";

    public const string FrameOptionsHeader = "X-Frame-Options";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var context = (HttpContext) state;
            var origins = context.RequestServices?.GetService<ServiceCorsOrigins>() ?? ServiceCorsOrigins.ForThisBuild;
            var ancestors = origins.FrameAncestors;
            var headers = context.Response.Headers;

            headers.Append(ContentSecurityPolicyHeader, $"frame-ancestors {ancestors}");

            if (ancestors == "'self'" && !headers.ContainsKey(FrameOptionsHeader))
            {
                headers[FrameOptionsHeader] = "SAMEORIGIN";
            }

            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}
