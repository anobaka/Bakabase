using System.Text.Json;
using Bakabase.Service.Components.CloudDownloads.Baidu;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class BaiduBridgeStorageTests
{
    [DataTestMethod]
    [DataRow("diskFull", "LOCAL_STORAGE_FULL")]
    [DataRow("quota", "LOCAL_STORAGE_FULL")]
    [DataRow("permission", "LOCAL_STORAGE_ACCESS_DENIED")]
    [DataRow("unavailable", "LOCAL_STORAGE_UNAVAILABLE")]
    [DataRow("unknown", "LOCAL_STORAGE_WRITE_FAILED")]
    public void OnlyStructuredLocalCausesBecomeStorageFailures(string kind, string expected)
    {
        using var data = JsonDocument.Parse(JsonSerializer.Serialize(new {code = "BAIDU_DOWNLOAD_FAILED", storage = new {kind}}));
        Assert.AreEqual(expected, BaiduPcsBridgeClient.StorageCode(data.RootElement));
    }
    [DataTestMethod]
    [DataRow("{\"code\":\"BAIDU_OUTPUT_DIRECTORY_FAILED\"}")]
    [DataRow("{\"code\":\"BAIDU_STORAGE_QUOTA_EXCEEDED\"}")]
    [DataRow("{\"storage\":{\"kind\":\"remoteQuota\"}}")]
    [DataRow("{\"storage\":null}")]
    public void LegacyDirectoryOrRemoteQuotaNeverBecomeLocalDiskFull(string json)
    {
        using var data = JsonDocument.Parse(json);
        Assert.IsNull(BaiduPcsBridgeClient.StorageCode(data.RootElement));
    }
}
