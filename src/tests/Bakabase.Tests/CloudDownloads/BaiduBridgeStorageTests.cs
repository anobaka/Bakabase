using System.Text.Json;
using Bakabase.Service.Components.CloudDownloads.Baidu;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class BaiduBridgeStorageTests
{
    [DataTestMethod]
    [DataRow("BAIDU_DOWNLOAD_CHECKSUM_MISMATCH", 19, 951, 885, 1, true, true)]
    [DataRow("BAIDU_DOWNLOAD_CHECKSUM_MISMATCH", 19, 951, 951, 2, true, false)]
    [DataRow("BAIDU_DOWNLOAD_CHECKSUM_MISMATCH", 20, 951, 885, 1, false, false)]
    [DataRow("BAIDU_EXISTING_CHECKSUM_UNAVAILABLE", 19, 951, 885, 1, true, false)]
    [DataRow("BAIDU_VERIFIED_FILE_MISSING", 1, 0, 0, 0, true, false)]
    [DataRow("BAIDU_UNKNOWN", 19, 951, 885, 1, false, false)]
    public void VerificationLocationRequiresConsistentOneBasedPositions(string code, int file, int blocks, int first,
        int wrong, bool member, bool mismatch)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new {manifestFileNumber = file, manifestFileCount = 19,
            checksumBlockCount = blocks, mismatchedBlockCount = wrong, firstMismatchedBlockNumber = first,
            firstMismatchedBlockBytes = 33554432, path = "private-file"}));
        var result = BaiduPcsBridgeClient.VerificationLocation(json.RootElement, code);
        Assert.AreEqual(member, result != null);
        Assert.AreEqual(mismatch, result?.ChecksumMismatch != null);
        if (mismatch)
        {
            Assert.AreEqual(19, result!.FileNumber);
            Assert.AreEqual(885, result.ChecksumMismatch!.FirstBlockNumber);
            Assert.AreEqual(33554432L, result.ChecksumMismatch.FirstBlockBytes);
        }
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("private-file"));
    }

    [DataTestMethod]
    [DataRow("BAIDU_VERIFIED_FILE_REPLACED", "165", 165)]
    [DataRow("BAIDU_OUTPUT_FILE_READ_FAILED", "1", 1)]
    [DataRow("BAIDU_VERIFIED_FILE_REPLACED", "-1", null)]
    [DataRow("BAIDU_VERIFIED_FILE_REPLACED", "2000001", null)]
    [DataRow("BAIDU_VERIFIED_FILE_REPLACED", "\"secret path\"", null)]
    [DataRow("untrusted message", "1", null)]
    public void ReportsOnlySafeManifestFileOrdinals(string code, string number, int? expected)
    {
        using var json = JsonDocument.Parse("{\"code\":" + JsonSerializer.Serialize(code) + ",\"manifestFileNumber\":" + number + "}");
        Assert.AreEqual(expected, BaiduPcsBridgeClient.RecheckedManifestFile(json.RootElement));
    }

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
    [DataRow("{\"storage\":{\"kind\":\"unknown\",\"operation\":\"recheckVerifiedFile\"}}")]
    [DataRow("{\"storage\":null}")]
    public void LegacyDirectoryOrRemoteQuotaNeverBecomeLocalDiskFull(string json)
    {
        using var data = JsonDocument.Parse(json);
        Assert.IsNull(BaiduPcsBridgeClient.StorageCode(data.RootElement));
    }
}
