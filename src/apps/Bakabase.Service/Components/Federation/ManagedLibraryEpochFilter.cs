using System.Threading.Tasks;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Federation;

/// <summary>Protect owner-local IDs against a restored/replaced data directory. Ordinary local APIs are unchanged.</summary>
public sealed class ManagedLibraryEpochFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var expected = context.HttpContext.Request.Query["managedLibraryEpoch"].ToString();
        if (string.IsNullOrEmpty(expected)) { await next(); return; }
        var identity = context.HttpContext.RequestServices.GetRequiredService<INodeIdentityProvider>();
        var current = await identity.GetAsync(context.HttpContext.RequestAborted);
        if (!NodeRequestSignature.IsIdentifier(expected) || current.LibraryEpoch != expected)
        {
            context.Result = new ConflictObjectResult(new {code = "LibraryEpochChanged",
                message = "This device's library was replaced. Refresh its resources before making changes."});
            return;
        }
        await next();
    }
}
