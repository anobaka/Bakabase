using System.Text.Json;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Federation.Queries;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Modules.Federation.Tests.Contracts;

[TestClass]
public class CommonQueryTests
{
    [TestMethod]
    public void ExplicitSourceWireValuesStayInSyncWithTheContentSourceEnum()
    {
        CollectionAssert.AreEquivalent(Enum.GetValues<ResourceSource>().Select(v => (int)v).ToArray(),
            QueryProtocol.SupportedSourceKinds.ToArray());
    }

    [TestMethod]
    public void UnknownPropertiesAreRejectedWithBothSerializers()
    {
        const string json = "{\"text\":\"safe\",\"customPropertyId\":7}";
        var system = System.Text.Json.JsonSerializer.Deserialize<CommonLibraryQuery>(json, FederationJson.Options)!;
        var newtonsoft = JsonConvert.DeserializeObject<CommonLibraryQuery>(json)!;
        foreach (var query in new[] { system, newtonsoft })
        {
            var error = Assert.ThrowsException<FederationQueryException>(() => QueryProtocol.Normalize(query, new()));
            Assert.AreEqual("UnsupportedQuery", error.Code);
            Assert.AreEqual("query.customPropertyId", error.Field);
        }
    }

    [TestMethod]
    public void ExportRejectsNodeListInsteadOfRecursivelySearching()
    {
        var request = System.Text.Json.JsonSerializer.Deserialize<NodeExportQuery>(
            "{\"expectedLibraryEpoch\":\"epoch\",\"nodeIds\":[\"third-node\"]}", FederationJson.Options)!;
        var error = Assert.ThrowsException<FederationQueryException>(() => QueryProtocol.RejectUnknown(request, "request"));
        Assert.AreEqual("request.nodeIds", error.Field);
    }

    [TestMethod]
    public void WireTypesAndUnknownSortOrSourceFailClosed()
    {
        Assert.ThrowsException<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<CommonLibraryQuery>(
            "{\"text\":42}", FederationJson.Options));
        foreach (var query in new[]
                 {
                     new CommonLibraryQuery { Sort = "Random" },
                     new CommonLibraryQuery { SourceKinds = [99] },
                     new CommonLibraryQuery { FileAvailability = "Playable" },
                     new CommonLibraryQuery { QueryContractVersion = 2 }
                 })
            Assert.ThrowsException<FederationQueryException>(() => QueryProtocol.Normalize(query, new()));
    }

    [TestMethod]
    public void NameAndFilenameAreOrButSourceAndFileFilterAreAnd()
    {
        var query = QueryProtocol.Normalize(new CommonLibraryQuery
            { Text = " CAFÉ ", SourceKinds = [2, 1, 2], FileAvailability = "HasFile" }, new());
        Assert.IsTrue(QueryProtocol.Matches(new(1, "Cafe\u0301", "different.mkv", true, [1]), query));
        Assert.IsTrue(QueryProtocol.Matches(new(1, "Other", "café.mkv", true, [2]), query));
        Assert.IsFalse(QueryProtocol.Matches(new(1, "café", null, false, [1]), query));
        Assert.IsFalse(QueryProtocol.Matches(new(1, "café", "café", true, []), query));
        Assert.IsFalse(QueryProtocol.Matches(new(1, "CA some FE", null, true, [1]), query));
        CollectionAssert.AreEqual(new[] { 1, 2 }, query.SourceKinds);
    }

    [TestMethod]
    public void SourceLessPlaceholderMatchesNameWithoutInventingAContentSource()
    {
        var query = QueryProtocol.Normalize(new CommonLibraryQuery { Text = "want", SourceKinds = [] }, new());
        Assert.IsTrue(QueryProtocol.Matches(new(7, "wanted work", null, false, []), query));
        Assert.IsFalse(QueryProtocol.Matches(new(7, null, null, false, []), query with { Text = "#7" }));
    }

    [TestMethod]
    public void DescendingKeepsNullLastAndReferenceTieKeysAscending()
    {
        var rows = new[] { Card("b", "x", 1), Card("a", "x", 2), Card("a", null, 3), Card("a", "z", 4) };
        Array.Sort(rows, QueryProtocol.Comparer("NameDesc"));
        CollectionAssert.AreEqual(new[] { 4, 2, 1, 3 }, rows.Select(r => r.Ref.ResourceId).ToArray());
    }

    private static FederatedResourceSummary Card(string node, string? title, int id) => new()
    {
        Ref = new(node, "epoch", id), Title = title, NormalizedSortKey = title, DisplayName = title ?? $"#{id}"
    };
}
