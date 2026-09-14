using System;
using System.Buffers;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostParserSerializationTests
{
    private const string ResultJson = """
        {"title":"Parsed title","resources":[{"link":"https://example.test/file.zip","code":"1234","password":"archive password","driveKind":10}],"optional":null,"flags":[true,12.5]}
        """;

    // AppStartup configures MVC with these settings; exercising Newtonsoft is essential because
    // its default reflection over JsonNode differs from SignalR's native System.Text.Json path.
    private static JsonSerializerSettings HttpSettings() => new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy {ProcessDictionaryKeys = false}
        },
        DateFormatString = "yyyy-MM-dd HH:mm:ss.fff",
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
    };

    private static PostParserTask LoadedTask() => new PostParserTaskDbModel
    {
        Id = 7, Source = PostParserSource.SoulPlus, Link = "https://example.test/post",
        Targets = "[1]", Results = "{\"DownloadInfo\":" + ResultJson + "}"
    }.ToDomainModel();

    [TestMethod]
    public void HttpListResponseWritesResultValuesInsteadOfJsonNodeImplementationProperties()
    {
        var response = new ListResponse<PostParserTask>([LoadedTask()]);
        var json = JObject.Parse(JsonConvert.SerializeObject(response, HttpSettings()));
        var result = json["data"]![0]!["results"]!["DownloadInfo"]!;
        Assert.IsTrue(JToken.DeepEquals(JToken.Parse(ResultJson), result));
        Assert.AreEqual(JTokenType.String, result["title"]!.Type);
        Assert.AreEqual("1234", result["resources"]![0]!["code"]!.Value<string>());
        Assert.AreEqual("archive password", result["resources"]![0]!["password"]!.Value<string>());
        Assert.AreEqual(JTokenType.Integer, result["resources"]![0]!["driveKind"]!.Type);
    }

    [TestMethod]
    public void HttpRoundTripPreservesNestedArraysPrimitivesAndNullResults()
    {
        var task = LoadedTask();
        task.Results![(PostParseTarget)2] = null;
        var json = JsonConvert.SerializeObject(task, HttpSettings());
        var restored = JsonConvert.DeserializeObject<PostParserTask>(json, HttpSettings())!;
        Assert.IsTrue(JsonNode.DeepEquals(task.Results[PostParseTarget.DownloadInfo], restored.Results![PostParseTarget.DownloadInfo]));
        Assert.IsNull(restored.Results[(PostParseTarget)2]);
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(task.ToDbModel().Results!), JObject.Parse(restored.ToDbModel().Results!)));
    }

    [TestMethod]
    public void SignalRIncrementalMessageMatchesTheHttpResultShape()
    {
        var options = new JsonHubProtocolOptions();
        options.PayloadSerializerOptions.DictionaryKeyPolicy = null;
        var protocol = new JsonHubProtocol(Options.Create(options));
        var buffer = new ArrayBufferWriter<byte>();
        var task = LoadedTask();
        protocol.WriteMessage(new InvocationMessage("GetIncrementalData", [nameof(PostParserTask), task]), buffer);
        // The JSON hub protocol terminates each message with a record separator.
        var hub = JObject.Parse(Encoding.UTF8.GetString(buffer.WrittenSpan[..^1]));
        var http = JObject.Parse(JsonConvert.SerializeObject(task, HttpSettings()));
        Assert.IsTrue(JToken.DeepEquals(http["results"], hub["arguments"]![1]!["results"]));
        Assert.AreEqual("archive password", hub["arguments"]![1]!["results"]!["DownloadInfo"]!["resources"]![0]!["password"]!.Value<string>());
    }
}
