using System.Reflection;
using Bakabase.Service.Components.CloudDownloads.Rclone;
using Bakabase.Service.Controllers;
using Bootstrap.Models.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class RcloneAuthorizationErrorsTests
{
    [DataTestMethod]
    [DataRow("RCLONE_BROWSER_AUTHORIZATION_STATE_MISMATCH")]
    [DataRow("RCLONE_BROWSER_AUTHORIZATION_CALLBACK_INVALID")]
    [DataRow("RCLONE_AUTHORIZATION_QUESTION_CHANGED")]
    public void ExpectedAuthorizationFailureReturnsOnlyStableMessageInTheNormalHttpEnvelope(string code)
    {
        var failure = ExpectedFailure(code);
        Assert.IsFalse(string.IsNullOrEmpty(failure.StackTrace), "Exercise an actually thrown exception, not an empty stack.");
        var context = Context(failure);
        new RcloneAuthorizationErrorsAttribute().OnException(context);
        Assert.IsTrue(context.ExceptionHandled);
        var result = (ObjectResult) context.Result!;
        var serialized = JsonConvert.SerializeObject(result.Value, new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver {NamingStrategy = new CamelCaseNamingStrategy()},
            DateFormatString = "yyyy-MM-dd HH:mm:ss.fff"
        });
        var wire = JObject.Parse(serialized);
        Assert.AreEqual((int) ResponseCode.InvalidPayloadOrOperation, wire["code"]!.Value<int>());
        Assert.AreEqual(code, wire["message"]!.Value<string>());
        Assert.IsTrue(wire["data"] == null || wire["data"]!.Type == JTokenType.Null);
        Assert.IsTrue(wire.Properties().All(p => p.Name is "code" or "message" or "data"));
        Assert.IsFalse(serialized.Contains(nameof(RcloneRemoteException)));
        Assert.IsFalse(serialized.Contains(nameof(ExpectedFailure)));
        Assert.IsFalse(serialized.Contains("stackTrace", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("no-store", context.HttpContext.Response.Headers.CacheControl.ToString());
    }

    [TestMethod]
    public void UnknownExceptionsIncludingLookalikeMessagesRemainUnhandled()
    {
        foreach (var failure in new Exception[]
        {
            new InvalidOperationException("RCLONE_BROWSER_AUTHORIZATION_STATE_MISMATCH"),
            new IOException("unexpected implementation failure", new RcloneRemoteException("RCLONE_AUTHORIZATION_INVALID"))
        })
        {
            var context = Context(failure);
            new RcloneAuthorizationErrorsAttribute().OnException(context);
            Assert.IsFalse(context.ExceptionHandled); Assert.IsNull(context.Result);
            Assert.AreSame(failure, context.Exception);
        }
    }

    [DataTestMethod]
    [DataRow(nameof(RcloneAccountsController.Start))]
    [DataRow(nameof(RcloneAccountsController.Get))]
    [DataRow(nameof(RcloneAccountsController.Answer))]
    [DataRow(nameof(RcloneAccountsController.Cancel))]
    [DataRow(nameof(RcloneAccountsController.StartBrowser))]
    [DataRow(nameof(RcloneAccountsController.SubmitBrowserCallback))]
    [DataRow(nameof(RcloneAccountsController.StopBrowser))]
    public void OnlyAuthorizationLifecycleActionsInstallTheExpectedFailureFilterAlongsideOriginProtection(string action)
    {
        var method = typeof(RcloneAccountsController).GetMethod(action)!;
        Assert.IsNotNull(method.GetCustomAttribute<RcloneAuthorizationErrorsAttribute>());
        Assert.IsNotNull(method.GetCustomAttribute<RcloneBrowserOriginAttribute>());
        Assert.IsNull(typeof(RcloneAccountsController).GetCustomAttribute<RcloneAuthorizationErrorsAttribute>());
        Assert.IsNull(typeof(RcloneAccountsController).GetMethod(nameof(RcloneAccountsController.List))!.GetCustomAttribute<RcloneAuthorizationErrorsAttribute>());
        Assert.IsNull(typeof(RcloneAccountsController).GetMethod(nameof(RcloneAccountsController.Save))!.GetCustomAttribute<RcloneAuthorizationErrorsAttribute>());
    }

    private static RcloneRemoteException ExpectedFailure(string code)
    { try { throw new RcloneRemoteException(code); } catch (RcloneRemoteException failure) { return failure; } }
    private static ExceptionContext Context(Exception failure) => new(new ActionContext(new DefaultHttpContext(), new RouteData(),
        new ControllerActionDescriptor {MethodInfo = typeof(RcloneAccountsController).GetMethod(nameof(RcloneAccountsController.SubmitBrowserCallback))!,
            ControllerTypeInfo = typeof(RcloneAccountsController).GetTypeInfo()}), []) {Exception = failure};
}
