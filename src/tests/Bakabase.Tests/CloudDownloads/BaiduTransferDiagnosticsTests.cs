using System.Text.Json;
using Bakabase.Service.Components.CloudDownloads.Baidu;
using Bakabase.Service.Components.Workflow;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public class BaiduTransferDiagnosticsTests
{
    [DataTestMethod]
    [DataRow("BAIDU_TRANSFER_ITEMS_REJECTED", """{"errno":407,"requested":2,"rejected":2,"itemErrnos":[-3,-2],"noAsyncTask":true}""", true)]
    [DataRow("BAIDU_TRANSFER_ITEMS_REJECTED", """{"errno":407,"requested":2,"rejected":1,"itemErrnos":[-3],"noAsyncTask":true}""", false)]
    [DataRow("BAIDU_TRANSFER_ITEMS_REJECTED", """{"errno":407,"requested":2,"rejected":2,"itemErrnos":[-3,0],"noAsyncTask":true}""", false)]
    [DataRow("BAIDU_TRANSFER_ITEMS_REJECTED", """{"errno":407,"requested":2,"rejected":2,"itemErrnos":[-3]}""", false)]
    [DataRow("BAIDU_TRANSFER_COUNT_LIMIT_CONFIRM_TARGET", """{"errno":12,"fileCount":40,"fileLimit":30,"requested":1}""", true)]
    [DataRow("BAIDU_TRANSFER_COUNT_LIMIT_CONFIRM_TARGET", """{"errno":12,"fileCount":4,"fileLimit":30,"requested":1}""", false)]
    [DataRow("BAIDU_TRANSFER_NAME_CONFLICT", """{"errno":12,"requested":1,"rejected":1,"itemErrnos":[-30],"noAsyncTask":true}""", true)]
    [DataRow("BAIDU_TRANSFER_OUTPUT_UNCONFIRMED", """{"errno":0,"requested":1}""", true)]
    [DataRow("BAIDU_TRANSFER_OUTPUT_UNCONFIRMED", """{"errno":12,"requested":1}""", false)]
    [DataRow("BAIDU_TRANSFER_RESPONSE_UNAVAILABLE", "{}", true)]
    [DataRow("BAIDU_TRANSFER_RESPONSE_UNAVAILABLE", """{"errno":0}""", false)]
    [DataRow("BAIDU_TRANSFER_PROVIDER_UNCONFIRMED", """{"errno":"PRIVATE_FILE_NAME"}""", false)]
    [DataRow("PRIVATE_FILE_NAME", """{"errno":12}""", false)]
    public void AcceptsOnlyCoherentNumericEvidence(string code, string evidence, bool expected)
    {
        var fields = JsonSerializer.Deserialize<Dictionary<string,object?>>(evidence)!;
        fields["code"] = code;
        fields["providerMessage"] = "PRIVATE_FILE_NAME";
        var result = BaiduTransferDiagnostics.Read(JsonSerializer.SerializeToElement(fields), code);
        Assert.AreEqual(expected, result != null);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("PRIVATE_FILE_NAME"));
        CollectionAssert.AreEqual(code == "PRIVATE_FILE_NAME" ? Array.Empty<string>() : new[] {code},
            WorkflowSourceRunProblems.Extract("PRIVATE_FILE_NAME " + code).ToArray());
    }
}
