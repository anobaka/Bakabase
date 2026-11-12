using System.Text.Json;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Tests;

[TestClass]
public class FileAssessmentLlmPolicyTests
{
    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { status = new { type = "string" } }, required = new[] { "status" }
    });

    [TestMethod]
    [DataRow("qwen-local-test", true, true)]
    [DataRow("qwen-local-test", false, false)]
    [DataRow("other-model", true, false)]
    public async Task OnlyTheStructuredLocalAssessmentGetsTemplateAndSchema(string model, bool structured, bool localTemplate)
    {
        await using var server = new FakeOpenAiServer();
        var harness = new LlmHarness(server.Endpoint);
        harness.DefaultConfig.ModelId = model;
        harness.DefaultConfig.MaxTokens = 18000;
        await harness.Service.CompleteForFeatureAsync(AiFeature.FileProcessor,
            [new ChatMessage(ChatRole.User, "Inspect current files")],
            new LlmModelParameters { UseJsonResponseFormat = true, JsonResponseSchema = structured ? Schema : null });
        var request = server.Requests.Single();
        var format = request.GetProperty("response_format");
        Assert.AreEqual(18000, request.GetProperty("max_completion_tokens").GetInt32());
        if (localTemplate)
        {
            Assert.AreEqual("none", request.GetProperty("reasoning_effort").GetString());
            Assert.IsFalse(request.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
            Assert.AreEqual("json_schema", format.GetProperty("type").GetString());
            var wrapper = format.GetProperty("json_schema");
            Assert.AreEqual("file_assessment", wrapper.GetProperty("name").GetString());
            Assert.IsTrue(JsonElement.DeepEquals(Schema, wrapper.GetProperty("schema")));
        }
        else
        {
            Assert.AreEqual("json_object", format.GetProperty("type").GetString());
            Assert.IsFalse(request.TryGetProperty("chat_template_kwargs", out _));
            Assert.IsFalse(request.TryGetProperty("reasoning_effort", out _));
        }
    }

    [TestMethod]
    public async Task FileAssessmentReceivesOnlyItsOwnAuditIdAndDoesNotInheritAnotherCallsIdOnCacheHit()
    {
        await using var server = new FakeOpenAiServer();
        var harness = new LlmHarness(server.Endpoint);
        harness.DefaultConfig.ModelId = "cacheable-model";
        var ids = new List<long>();
        var parameters = new LlmModelParameters
            { UseJsonResponseFormat = true, JsonResponseSchema = Schema, OnUsageRecorded = ids.Add };
        async Task Inspect(string input) => await harness.Service.CompleteForFeatureAsync(AiFeature.FileProcessor,
            [new ChatMessage(ChatRole.User, input)], parameters);
        await Inspect("first");
        await Inspect("first");
        await Inspect("second");
        Assert.AreEqual(2, server.Requests.Count);
        CollectionAssert.AreEqual(new long[] { 1, 2 }, ids);
        server.Respond = _ => Task.FromResult((503, "busy"));
        await Assert.ThrowsAsync<Exception>(() => Inspect("third"));
        CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, ids);
    }

    [TestMethod]
    public async Task TransientHttpErrorDoesNotStartASecondFileAssessment()
    {
        await using var server = new FakeOpenAiServer();
        server.Respond = _ => Task.FromResult((503, "{\"error\":{\"message\":\"busy\"}}"));
        var harness = new LlmHarness(server.Endpoint);
        await Assert.ThrowsAsync<Exception>(() => harness.Service.CompleteForFeatureAsync(AiFeature.FileProcessor,
            [new ChatMessage(ChatRole.User, "Inspect files")],
            new LlmModelParameters { UseJsonResponseFormat = true, JsonResponseSchema = Schema })
            .WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, server.Requests.Count);
    }
}
