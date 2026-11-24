using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Service.Components.CloudDownloads.Baidu;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class BaiduBridgeFileIoTests
{
    [TestMethod]
    public void Only_proven_reads_are_reported_for_each_file_and_attempt()
    {
        var provider = new FileIoObservationProvider();
        using var scope = FileIoObservationScope.Enter(provider, new("downloadTask", "task-a"));
        using var reads = new BaiduPcsBridgeClient.VerificationReadObservation();

        Record(reads, """{"stage":"downloading","completedBytes":100,"verifiedBytes":50,"verificationTotalBytes":150}""");
        Record(reads, """{"stage":"verifying"}""");
        RecordVerification(reads, 50, 150);
        Assert.AreEqual(0, provider.Drain().Count);

        RecordVerification(reads, 0, 150);
        RecordVerification(reads, 32, 150);
        RecordVerification(reads, 32, 150);
        AssertRead(provider, 32);
        RecordVerification(reads, 150, 150);
        RecordVerification(reads, 150, 150);
        AssertRead(provider, 118);

        RecordVerification(reads, 0, 40);
        RecordVerification(reads, 14, 40);
        RecordVerification(reads, 0, 40); // A real second pass rereads the bytes.
        RecordVerification(reads, 40, 40);
        AssertRead(provider, 54);
        Assert.AreEqual(0, provider.Drain().Count);
    }

    [TestMethod]
    public void Invalid_sequences_and_new_bridge_processes_need_a_new_zero()
    {
        var provider = new FileIoObservationProvider();
        using var scope = FileIoObservationScope.Enter(provider, new("downloadTask", "task-a"));
        using (var reads = new BaiduPcsBridgeClient.VerificationReadObservation())
        {
            RecordVerification(reads, 0, 80);
            RecordVerification(reads, 30, 80);
            RecordVerification(reads, 20, 80);
            RecordVerification(reads, 50, 80);

            RecordVerification(reads, 0, 80);
            RecordVerification(reads, 15, 81);
            RecordVerification(reads, 50, 80);

            RecordVerification(reads, 0, 80);
            Record(reads, """{"stage":"downloading","completedBytes":200}""");
            RecordVerification(reads, 71, 80);

            RecordVerification(reads, 0, 80);
            Record(reads, """{"stage":"verifying","verifiedBytes":"40","verificationTotalBytes":80}""");
            RecordVerification(reads, 80, 80);

            RecordVerification(reads, 0, 80);
            RecordVerification(reads, 20, 80);
        }
        AssertRead(provider, 50);

        using var next = new BaiduPcsBridgeClient.VerificationReadObservation();
        RecordVerification(next, 80, 80);
        Assert.AreEqual(0, provider.Drain().Count);
    }

    private static void RecordVerification(BaiduPcsBridgeClient.VerificationReadObservation reads, long value, long total) =>
        Record(reads, JsonSerializer.Serialize(new {stage = "verifying", verifiedBytes = value, verificationTotalBytes = total}));

    private static void Record(BaiduPcsBridgeClient.VerificationReadObservation reads, string json)
    {
        using var document = JsonDocument.Parse(json);
        reads.Record(document.RootElement);
    }

    private static void AssertRead(FileIoObservationProvider provider, long expected)
    {
        var sample = provider.Drain().Single();
        Assert.AreEqual("downloadTask", sample.OwnerKind);
        Assert.AreEqual("task-a", sample.OwnerId);
        Assert.AreEqual(expected, sample.ReadBytes);
        Assert.AreEqual(0L, sample.WrittenBytes);
        Assert.IsTrue(sample.ReadObserved);
        Assert.IsFalse(sample.WriteObserved);
    }
}
