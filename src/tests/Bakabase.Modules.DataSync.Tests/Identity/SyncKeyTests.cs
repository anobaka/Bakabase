using Bakabase.Modules.DataSync.Identity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Identity;

[TestClass]
public class SyncKeyTests
{
    [TestMethod]
    [DataRow("0123456789abcdef0123456789abcdef", true)]
    [DataRow("0123456789ABCDEF0123456789ABCDEF", false)]
    [DataRow("0123456789abcdef0123456789abcde", false)]
    [DataRow("0123456789abcdef0123456789abcdef0", false)]
    [DataRow("0123456789abcdef-123456789abcdef", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void FormatValidation(string? value, bool valid)
    {
        Assert.AreEqual(valid, SyncKey.IsValid(value));
        if (valid) Assert.AreEqual(value, new SyncKey(value!).Value);
        else Assert.ThrowsException<ArgumentException>(() => new SyncKey(value!));
    }

    [TestMethod]
    public void NewKeysAreValidAndUnique()
    {
        var keys = new HashSet<SyncKey>();
        for (var i = 0; i < 10_000; i++)
        {
            var key = SyncKey.New();
            Assert.IsTrue(SyncKey.IsValid(key.Value));
            Assert.AreNotEqual(SyncKey.LinkLevel, key);
            Assert.IsTrue(keys.Add(key));
        }
    }

    [TestMethod]
    public void LinkLevelIsThirtyTwoZeros()
    {
        Assert.AreEqual(new string('0', 32), SyncKey.LinkLevel.Value);
        Assert.AreEqual(new SyncKey(new string('0', 32)), SyncKey.LinkLevel);
        Assert.IsTrue(SyncKey.IsValid(SyncKey.LinkLevel.Value));
    }

    [TestMethod]
    public void EntityKeysPreservesOrder()
    {
        var primary = new SyncKey("ffffffffffffffffffffffffffffffff");
        var alias = new SyncKey("00000000000000000000000000000001");
        var keys = new EntityKeys([primary, alias]);
        Assert.AreEqual(primary, keys.Primary);
        CollectionAssert.AreEqual(new[] { primary, alias }, keys.All.ToArray());
        Assert.IsTrue(keys.Contains(alias));
        Assert.IsFalse(keys.Contains(SyncKey.LinkLevel));
        Assert.IsNull(EntityKeys.None.Primary);
        Assert.AreEqual(0, EntityKeys.None.All.Count);
    }
}
