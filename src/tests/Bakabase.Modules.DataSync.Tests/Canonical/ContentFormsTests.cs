using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Canonical;

[TestClass]
public class ContentFormsTests
{
    [TestMethod]
    public void SharedHashIgnoresChildIdsAndLocalOrderButNotTheOrderKey()
    {
        IDataSyncKindCodec codec = TestItemCodec.Instance;
        var a = new TestItemContent("Genre", null, [new TestChild("1", "Action"), new TestChild("2", "Drama")]);
        var b = new TestItemContent("Genre", null, [new TestChild("x", "Drama"), new TestChild("y", "Action")]);
        Assert.AreEqual(codec.SharedHash(a, "a0", false), codec.SharedHash(b, "a0", false));
        Assert.AreNotEqual(codec.SharedHash(a, "a0", false), codec.SharedHash(a, "a1", false));
        Assert.AreEqual(ContentHash.Of(codec.ComparisonForm(a, "a0", false)), codec.SharedHash(a, "a0", false));
    }
}
