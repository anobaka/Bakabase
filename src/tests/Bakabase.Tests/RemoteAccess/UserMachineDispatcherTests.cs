using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Remoting.Components.Forwarding;
using Bakabase.Remoting.Components.UserMachine;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// Which requests the client claims, and what it says about the ones it cannot run yet.
/// </summary>
/// <remarks>
/// Deliberately built from a handler set the test controls rather than the real one: the
/// interesting case is a declared route with no handler, and the real client is expected
/// to grow handlers for all of them, at which point a test written against the real set
/// would have nothing left to assert.
/// </remarks>
[TestClass]
public class UserMachineDispatcherTests
{
    private sealed class StubHandler(string routeKey) : IUserMachineHandler
    {
        public string RouteKey => routeKey;
        public readonly List<IReadOnlyDictionary<string, string>> Calls = [];

        public Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
        {
            Calls.Add(routeValues);
            context.Response.StatusCode = (int) HttpStatusCode.OK;

            return Task.CompletedTask;
        }
    }

    private static UserMachineDispatcher Dispatcher(params IUserMachineHandler[] handlers) =>
        new(handlers, NullLogger<UserMachineDispatcher>.Instance);

    private static DefaultHttpContext Request(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        return context;
    }

    [TestMethod]
    public async Task A_route_nobody_here_handles_says_the_client_is_behind()
    {
        // Never forwarded: the server refuses these too, so a round trip would only turn
        // "this client cannot do that yet" into a refusal that blames the server.
        var context = Request("POST", "/tool/cookie-capture");

        Assert.IsTrue(await Dispatcher().TryHandleAsync(context));

        context.Response.Body.Position = 0;
        var body = JsonDocument.Parse(new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEnd())
            .RootElement;

        Assert.AreEqual((int) HttpStatusCode.NotImplemented, context.Response.StatusCode);
        Assert.AreEqual(nameof(ClientForwardingFailure.NeedsNewerClient),
            context.Response.Headers["X-Bakabase-Client"].ToString());
        var message = body.GetProperty("message").GetString()!;
        StringAssert.Contains(message, "/tool/cookie-capture");
        // The desktop app runs it; there is no separate client to update.
        StringAssert.Contains(message, "Updating Bakabase on this computer");
        Assert.IsFalse(message.Contains("client", StringComparison.OrdinalIgnoreCase), message);
    }

    [TestMethod]
    public async Task A_route_with_a_handler_reaches_it_with_its_captured_segments()
    {
        var handler = new StubHandler("POST /player/playlist/{playlistId:int}/batch-play");
        var context = Request("POST", "/player/playlist/12/batch-play");

        Assert.IsTrue(await Dispatcher(handler).TryHandleAsync(context));
        Assert.AreEqual("12", handler.Calls[0]["playlistId"]);
    }

    [TestMethod]
    public async Task Anything_the_table_does_not_list_is_left_to_the_server()
    {
        var context = Request("GET", "/resource/search");

        Assert.IsFalse(await Dispatcher().TryHandleAsync(context));
    }

    [TestMethod]
    public void A_handler_for_a_route_nobody_declared_fails_at_startup()
    {
        // It would sit unreachable forever otherwise, which shows up only as a feature
        // quietly not working.
        var e = Assert.ThrowsException<InvalidOperationException>(
            () => Dispatcher(new StubHandler("GET /resource/invented")));

        StringAssert.Contains(e.Message, "/resource/invented");
    }
}
