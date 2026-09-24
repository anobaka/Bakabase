using System;
using System.Linq;
using Bakabase.Remoting.Components.UserMachine;
using Bakabase.Service.Components.RemoteAccess;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// Holds the client's route table against the server's catalog.
/// </summary>
/// <remarks>
/// The two are separate lists because the server discovers its own by reflecting over
/// its controllers, which the client assembly cannot see. This test lives in the one
/// assembly that references both, and it is the only thing keeping them equal.
/// <para>
/// Both directions of drift are bugs, and they fail differently. A route the server
/// marks but the client does not gets forwarded and refused — visible, but the feature
/// is dead on the client. A route the client claims but the server does not mark gets
/// intercepted here and never reaches the server that was going to handle it.
/// </para>
/// </remarks>
[TestClass]
public class UserMachineRouteTableTests
{
    [TestMethod]
    public void The_client_table_and_the_server_catalog_agree()
    {
        var server = UserMachineEndpointCatalog.Entries.Select(e => e.Key).ToArray();
        var client = UserMachineRoutes.All.Select(r => r.Key).ToArray();

        var onlyOnServer = server.Except(client, StringComparer.OrdinalIgnoreCase).ToArray();
        var onlyOnClient = client.Except(server, StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.IsTrue(onlyOnServer.Length == 0 && onlyOnClient.Length == 0,
            $"the two lists drifted.{Environment.NewLine}" +
            $"the server marks these and the client would forward them: {string.Join(", ", onlyOnServer)}" +
            $"{Environment.NewLine}" +
            $"the client claims these and the server does not mark them: {string.Join(", ", onlyOnClient)}");
    }

    [TestMethod]
    public void No_route_is_listed_twice()
    {
        var duplicates = UserMachineRoutes.All
            .GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.AreEqual(0, duplicates.Length, string.Join(", ", duplicates));
    }

    // ---- matching ----

    [TestMethod]
    public void A_literal_route_matches_itself_and_nothing_near_it()
    {
        Assert.IsNotNull(UserMachineRoutes.Match("GET", "/gui/url"));
        Assert.IsNull(UserMachineRoutes.Match("POST", "/gui/url"));
        Assert.IsNull(UserMachineRoutes.Match("GET", "/gui/url/extra"));
        Assert.IsNull(UserMachineRoutes.Match("GET", "/gui"));
    }

    [TestMethod]
    public void A_parameter_is_captured()
    {
        var match = UserMachineRoutes.Match("GET", "/resource/42/play");

        Assert.IsNotNull(match);
        Assert.AreEqual("/resource/{resourceId}/play", match.Route.Template);
        Assert.AreEqual("42", match.Values["resourceId"]);
    }

    [TestMethod]
    public void An_int_constraint_is_honoured()
    {
        // Without this, a path the server routes elsewhere entirely would be intercepted
        // here and answered by a handler that was never meant to see it.
        Assert.IsNotNull(UserMachineRoutes.Match("POST", "/player/playlist/7/batch-play"));
        Assert.IsNull(UserMachineRoutes.Match("POST", "/player/playlist/latest/batch-play"));
    }

    [TestMethod]
    public void A_longer_route_is_not_swallowed_by_a_shorter_one()
    {
        // /player/batch-play and /player/batch-play/candidates are both in the table,
        // and a prefix match would collapse them.
        Assert.AreEqual("/player/batch-play",
            UserMachineRoutes.Match("POST", "/player/batch-play")!.Route.Template);
        Assert.AreEqual("/player/batch-play/candidates",
            UserMachineRoutes.Match("POST", "/player/batch-play/candidates")!.Route.Template);
    }

    [TestMethod]
    public void An_encoded_segment_is_handed_over_decoded()
    {
        // A DLsite work id is opaque; whatever the frontend escaped, the handler wants
        // the real value.
        var match = UserMachineRoutes.Match("POST", "/dlsite-work/RJ%20123/launch");

        Assert.IsNotNull(match);
        Assert.AreEqual("RJ 123", match.Values["workId"]);
    }

    [TestMethod]
    public void Casing_and_trailing_slashes_do_not_decide_anything()
    {
        // Routes match without regard to case, and a controller written as
        // [Route("~/[controller]")] yields the class name's casing.
        Assert.IsNotNull(UserMachineRoutes.Match("get", "/GUI/URL"));
        Assert.IsNotNull(UserMachineRoutes.Match("GET", "/gui/url/"));
        Assert.IsNotNull(UserMachineRoutes.Match("GET", "/Tampermonkey/install"));
    }

    [TestMethod]
    public void Ordinary_requests_pass_straight_through()
    {
        foreach (var path in new[] {"/resource/search", "/file/raw", "/remote-access/context", "/", "/options"})
        {
            Assert.IsNull(UserMachineRoutes.Match("GET", path), path);
        }
    }
}
