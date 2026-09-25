using Bakabase.Modules.DataSync.Identity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Identity;

[TestClass]
public class ActorIdTests
{
    // Expected ids computed outside .NET: the first 16 hex of
    // printf 'bakabase-datasync-actor\n%s\n%s\n%s' nodeId epoch salt | shasum -a 256
    [TestMethod]
    [DataRow("node-a", "epoch-1", "0123456789abcdef", "cc529255fcf3f9d2")]
    [DataRow("5f0e3c1a9b8d4e7fa2c6b1d0e9f8a7b6", "0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e", "fedcba9876543210",
        "ef06e091ec880255")]
    [DataRow("ノード", "epoch", "00000000000000ff", "0b1350a95804ab69")]
    public void DerivationMatchesTheFixedVectors(string nodeId, string epoch, string salt, string expected)
    {
        Assert.AreEqual(expected, DataSyncActorId.Derive(nodeId, epoch, salt).Value);
    }

    [TestMethod]
    public void EveryInputChangesTheActor()
    {
        var baseline = DataSyncActorId.Derive("node", "epoch", "0123456789abcdef");
        Assert.AreNotEqual(baseline, DataSyncActorId.Derive("node2", "epoch", "0123456789abcdef"));
        Assert.AreNotEqual(baseline, DataSyncActorId.Derive("node", "epoch2", "0123456789abcdef"));
        Assert.AreNotEqual(baseline, DataSyncActorId.Derive("node", "epoch", "0123456789abcdee"));
        Assert.AreEqual(baseline, DataSyncActorId.Derive("node", "epoch", "0123456789abcdef"));
    }

    [TestMethod]
    [DataRow("0123456789ABCDEF")]
    [DataRow("0123456789abcde")]
    [DataRow("0123456789abcdef0")]
    [DataRow("0123456789abcdeg")]
    [DataRow("")]
    public void DeriveRefusesASaltThatIsNot16LowercaseHex(string salt)
    {
        Assert.ThrowsException<ArgumentException>(() => DataSyncActorId.Derive("node", "epoch", salt));
    }

    [TestMethod]
    public void DeriveRefusesAmbiguousOrEmptyInputs()
    {
        Assert.ThrowsException<ArgumentException>(() => DataSyncActorId.Derive("", "epoch", "0123456789abcdef"));
        Assert.ThrowsException<ArgumentException>(() => DataSyncActorId.Derive("node", "", "0123456789abcdef"));
        Assert.ThrowsException<ArgumentException>(() => DataSyncActorId.Derive("a\nb", "c", "0123456789abcdef"));
        Assert.ThrowsException<ArgumentException>(() => DataSyncActorId.Derive("a", "b\nc", "0123456789abcdef"));
    }

    [TestMethod]
    [DataRow("0123456789abcdef", true)]
    [DataRow("ffffffffffffffff", true)]
    [DataRow("0123456789ABCDEF", false)]
    [DataRow("0123456789abcde", false)]
    [DataRow("0123456789abcdef0", false)]
    [DataRow("0123456789abcdeg", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void ValidityRules(string? value, bool valid)
    {
        Assert.AreEqual(valid, DataSyncActorId.IsValid(value));
        if (valid) Assert.AreEqual(value, new DataSyncActorId(value!).ToString());
        else Assert.ThrowsException<ArgumentException>(() => new DataSyncActorId(value!));
    }

    [TestMethod]
    public void ActorIdsCompareByValue()
    {
        Assert.AreEqual(new DataSyncActorId("0123456789abcdef"), new DataSyncActorId("0123456789abcdef"));
        Assert.AreNotEqual(new DataSyncActorId("0123456789abcdef"), new DataSyncActorId("0123456789abcdee"));
    }

    [TestMethod]
    public void NewSaltIs16HexAndNeverRepeats()
    {
        var salts = new HashSet<string>();
        for (var i = 0; i < 10_000; i++)
        {
            var salt = DataSyncActorId.NewSalt();
            Assert.IsTrue(DataSyncActorId.IsValid(salt), salt);
            Assert.IsTrue(salts.Add(salt), $"Salt {salt} repeated.");
        }
    }
}
