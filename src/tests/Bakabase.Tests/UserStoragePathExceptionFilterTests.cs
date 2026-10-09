using Bakabase.Abstractions.Exceptions;
using Bakabase.Service.Components;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Bakabase.Tests;

[TestClass]
public sealed class UserStoragePathExceptionFilterTests
{
    private static ExceptionContext Context(Exception exception) => new(
        new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
    {
        Exception = exception
    };

    [TestMethod]
    public void StorageSelectionErrorReturnsBadRequestWithOnlyItsUserMessage()
    {
        var error = new UserStoragePathException("Choose a folder inside a mounted storage location. /tmp");
        try { throw error; }
        catch (UserStoragePathException) { }
        Assert.IsNotNull(error.StackTrace);
        Assert.IsInstanceOfType<IUserActionableException>(error);
        Assert.IsInstanceOfType<IOException>(error);
        var context = Context(error);

        new UserStoragePathExceptionFilter().OnException(context);

        Assert.IsTrue(context.ExceptionHandled);
        var result = (ObjectResult)context.Result!;
        Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
        var response = (BaseResponse)result.Value!;
        Assert.AreEqual(400, response.Code);
        Assert.AreEqual(error.Message, response.Message);
        Assert.IsFalse(response.Message!.Contains(nameof(UserStoragePathExceptionFilterTests)));
    }

    [TestMethod]
    public void RealIoFailuresAndOtherExceptionsKeepTheirExistingHandling()
    {
        foreach (var error in new Exception[]
                 {
                     new IOException("disk write failed"), new UnauthorizedAccessException("permission denied"),
                     new DiskWriteException("/storage/file", true),
                     new InvalidOperationException("wrapped", new UserStoragePathException("inner selection error"))
                 })
        {
            var context = Context(error);
            new UserStoragePathExceptionFilter().OnException(context);
            Assert.IsFalse(context.ExceptionHandled, error.GetType().Name);
            Assert.IsNull(context.Result, error.GetType().Name);
            Assert.AreSame(error, context.Exception);
        }
    }
}
