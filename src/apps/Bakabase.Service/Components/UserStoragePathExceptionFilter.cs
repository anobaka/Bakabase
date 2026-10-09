using Bakabase.Abstractions.Exceptions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Bakabase.Service.Components;

/// <summary>Returns storage selection errors before the legacy middleware adds a stack trace.</summary>
public sealed class UserStoragePathExceptionFilter : IExceptionFilter, IOrderedFilter
{
    public int Order => int.MaxValue;

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not UserStoragePathException error) return;
        if (context.HttpContext.Response.HasStarted) context.HttpContext.Abort();
        else context.Result = new ObjectResult(new BaseResponse(StatusCodes.Status400BadRequest, error.Message))
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
        context.ExceptionHandled = true;
    }
}
