using Bakabase.Modules.DataSync.Identity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests;

/// <summary>
/// The module's half of the naming rule (§2): the constants generator emits every public enum of every assembly
/// the Service references, so each one carries the DataSync prefix, and so does every HTTP record.
/// </summary>
[TestClass]
public class NamingTests
{
    private static readonly Type[] PublicTypes = typeof(DataSyncVersionVector).Assembly.GetExportedTypes();

    [TestMethod]
    public void EveryPublicEnumStartsWithDataSync()
    {
        var offenders = PublicTypes.Where(t => t.IsEnum && !t.Name.StartsWith("DataSync", StringComparison.Ordinal))
            .Select(t => t.FullName).ToArray();
        Assert.AreEqual(0, offenders.Length, string.Join(", ", offenders));
    }

    [TestMethod]
    public void EveryServiceTypeStartsWithDataSync()
    {
        var offenders = PublicTypes
            .Where(t => t.Namespace == "Bakabase.Modules.DataSync.Services")
            .Where(t => !t.Name.StartsWith(t.IsInterface ? "IDataSync" : "DataSync", StringComparison.Ordinal))
            .Select(t => t.FullName).ToArray();
        Assert.AreEqual(0, offenders.Length, string.Join(", ", offenders));
    }
}
