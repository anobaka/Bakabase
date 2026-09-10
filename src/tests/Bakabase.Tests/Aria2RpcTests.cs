using System;
using System.Text.Json.Nodes;
using Bakabase.Service.Components.Acquisition;

namespace Bakabase.Tests;

/// <summary>
/// Talking to aria2 about a magnet.
/// <para>
/// A magnet handed to the wrong place downloads gigabytes into somewhere nobody looks, and a
/// magnet whose second half is never followed reads as finished with nothing on disk. Neither is
/// something a live daemon would tell you about, so the request bodies and the replies are what
/// gets pinned down here.
/// </para>
/// </summary>
[TestClass]
public sealed class Aria2RpcTests
{
    [TestMethod]
    public void TheDownloadIsAskedForWhereTheRunCanFindIt()
    {
        var body = JsonNode.Parse(Aria2Rpc.BuildAddUri("magnet:?xt=urn:btih:abc", "/runs/17", null, "add"))!
            .AsObject();

        Assert.AreEqual("aria2.addUri", body["method"]!.GetValue<string>());

        var parameters = body["params"]!.AsArray();

        Assert.AreEqual("magnet:?xt=urn:btih:abc", parameters[0]!.AsArray()[0]!.GetValue<string>());
        Assert.AreEqual("/runs/17", parameters[1]!["dir"]!.GetValue<string>(),
            "a step must not write outside its own working directory");
    }

    /// <summary>
    /// A daemon started with --rpc-secret rejects anything else, and one started without it ignores
    /// the parameter — so sending it whenever it is configured is always right.
    /// </summary>
    [TestMethod]
    public void TheTokenLeadsWhenThereIsOne()
    {
        var parameters = JsonNode
            .Parse(Aria2Rpc.BuildAddUri("magnet:?xt=urn:btih:abc", "/runs/17", "s3cret", "add"))!
            .AsObject()["params"]!.AsArray();

        Assert.AreEqual("token:s3cret", parameters[0]!.GetValue<string>());
        Assert.AreEqual("magnet:?xt=urn:btih:abc", parameters[1]!.AsArray()[0]!.GetValue<string>());
        Assert.AreEqual("/runs/17", parameters[2]!["dir"]!.GetValue<string>());
    }

    [TestMethod]
    public void AskingAboutOneDownloadNamesIt()
    {
        var parameters = JsonNode.Parse(Aria2Rpc.BuildTellStatus("2089b05ecca3d829", "s3cret", "status"))!
            .AsObject()["params"]!.AsArray();

        Assert.AreEqual("token:s3cret", parameters[0]!.GetValue<string>());
        Assert.AreEqual("2089b05ecca3d829", parameters[1]!.GetValue<string>());
    }

    [TestMethod]
    public void TheIdComesBackFromAnAcceptedDownload()
    {
        Assert.AreEqual("2089b05ecca3d829",
            Aria2Rpc.ReadGid("""{"id":"add","jsonrpc":"2.0","result":"2089b05ecca3d829"}"""));
    }

    /// <summary>
    /// The half that matters. A magnet's first download is the torrent's metadata: it completes in
    /// seconds, writes nothing anyone wants, and names the download the files actually arrive under.
    /// </summary>
    [TestMethod]
    public void AFinishedMetadataDownloadPointsAtTheRealOne()
    {
        var status = Aria2Rpc.ReadStatus(
            """
            {"id":"status","jsonrpc":"2.0","result":{
              "gid":"2089b05ecca3d829",
              "status":"complete",
              "completedLength":"229",
              "totalLength":"229",
              "followedBy":["a4c4d6dc0e2b8a1f"],
              "files":[{"path":"/runs/17/abc.torrent"}]
            }}
            """);

        Assert.IsTrue(status.IsComplete);
        Assert.AreEqual("a4c4d6dc0e2b8a1f", status.FollowedBy,
            "without following this, the run would call a torrent finished with nothing on disk");
    }

    [TestMethod]
    public void AFinishedTorrentSaysWhatItWrote()
    {
        var status = Aria2Rpc.ReadStatus(
            """
            {"id":"status","jsonrpc":"2.0","result":{
              "gid":"a4c4d6dc0e2b8a1f",
              "status":"complete",
              "completedLength":"4294967296",
              "totalLength":"4294967296",
              "files":[{"path":"/runs/17/Some Work/disc1.iso"},{"path":"/runs/17/Some Work/readme.txt"}]
            }}
            """);

        Assert.IsTrue(status.IsComplete);
        Assert.IsNull(status.FollowedBy);
        Assert.AreEqual(2, status.Files.Count);
        Assert.AreEqual(4294967296L, status.TotalLength,
            "byte counts outgrow a 32-bit number, which is why aria2 sends them as strings");
        Assert.AreEqual(100, status.Percentage);
    }

    [TestMethod]
    public void ProgressIsZeroUntilTheSizeIsKnown()
    {
        var status = Aria2Rpc.ReadStatus(
            """
            {"id":"status","jsonrpc":"2.0","result":{"status":"active","completedLength":"0","totalLength":"0","files":[]}}
            """);

        Assert.AreEqual(0, status.Percentage, "a magnet has no size until its metadata resolves");
        Assert.IsFalse(status.IsComplete);
        Assert.IsFalse(status.IsFailed);
    }

    [TestMethod]
    public void AStoppedDownloadIsAFailureWithAReason()
    {
        var status = Aria2Rpc.ReadStatus(
            """
            {"id":"status","jsonrpc":"2.0","result":{"status":"error","completedLength":"0","totalLength":"0","errorMessage":"Timeout.","files":[]}}
            """);

        Assert.IsTrue(status.IsFailed);
        Assert.AreEqual("Timeout.", status.ErrorMessage);
    }

    /// <summary>
    /// A wrong token comes back as a JSON-RPC error rather than an HTTP one, so it has to be read
    /// out of the body or the run would treat "unauthorized" as "accepted".
    /// </summary>
    [TestMethod]
    public void ARefusalIsRaisedRatherThanRead()
    {
        var refusal = """{"id":"add","jsonrpc":"2.0","error":{"code":1,"message":"Unauthorized"}}""";

        var error = Assert.ThrowsException<InvalidOperationException>(() => Aria2Rpc.ReadGid(refusal));

        StringAssert.Contains(error.Message, "Unauthorized");
    }
}
