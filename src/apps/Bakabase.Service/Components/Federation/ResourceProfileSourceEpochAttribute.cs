using System;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Federation;

/// <summary>Source-owned integer IDs may only be read under the library incarnation the viewer selected.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ResourceProfileSourceEpochAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var expected = context.HttpContext.Request.Query["libraryEpoch"].ToString();
        if (!string.IsNullOrEmpty(expected))
        {
            var current = await context.HttpContext.RequestServices.GetRequiredService<INodeIdentityProvider>()
                .GetAsync(context.HttpContext.RequestAborted);
            if (!NodeRequestSignature.IsIdentifier(expected) || current.LibraryEpoch != expected)
            {
                context.Result = new ConflictObjectResult(new {code = "LibraryEpochChanged",
                    message = "This source library was replaced. Review its resource profile filters before using them."});
                return;
            }
        }
        var executed = await next();
        if (executed.Exception == null && !string.IsNullOrEmpty(expected))
        {
            var current = await context.HttpContext.RequestServices.GetRequiredService<INodeIdentityProvider>()
                .GetAsync(context.HttpContext.RequestAborted);
            if (current.LibraryEpoch != expected)
                executed.Result = new ConflictObjectResult(new {code = "LibraryEpochChanged",
                    message = "This source library changed while its resource profiles were being read. Retry after reviewing the source."});
        }
    }
}
