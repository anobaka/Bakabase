using Bakabase.Service.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class ServerListeningPortsTests
{
    [TestMethod]
    public void UsesStableDefaultForHeadlessSourceRuns() =>
        CollectionAssert.AreEqual(new[] { 34567 }, BakabaseHost.ParseServerListeningPorts(null).ToArray());

    [TestMethod]
    public void HonorsBothDocumentedSeparatorsAndRemovesDuplicates() =>
        CollectionAssert.AreEqual(new[] { 34567, 8080 }, BakabaseHost.ParseServerListeningPorts("34567; 8080,34567").ToArray());

    [TestMethod]
    [DataRow("0")]
    [DataRow("65536")]
    [DataRow("8080,")]
    [DataRow("invalid")]
    public void RejectsInvalidPortsInsteadOfFallingBackToKestrelDefault(string value) =>
        Assert.ThrowsExactly<ArgumentException>(() => BakabaseHost.ParseServerListeningPorts(value));
}
