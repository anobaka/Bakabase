using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>
/// §3.4: <see cref="DataSyncLabelKey"/>'s comparer is the Property module's <c>GetLabelComparer()</c> —
/// <see cref="StringComparer.OrdinalIgnoreCase"/> with IgnoreCase on, <see cref="StringComparer.Ordinal"/> with it off —
/// and what its keys look like. That <see cref="DataSyncLabelKey.Fold"/> agrees with the comparer over every BMP code
/// unit, supplementary characters and real labels is checked against the Property module itself, in
/// <c>Bakabase.Tests</c>' <c>LabelKeyCrossCheckTests</c>.
/// </summary>
[TestClass]
public class LabelKeyCrossCheckTests
{
    [TestMethod]
    public void TheComparerIsThePropertyModulesLabelComparer()
    {
        // GetLabelComparer: options?.IgnoreCase == true ? OrdinalIgnoreCase : Ordinal (F72).
        Assert.AreSame(StringComparer.OrdinalIgnoreCase, DataSyncLabelKey.ComparerFor(true));
        Assert.AreSame(StringComparer.Ordinal, DataSyncLabelKey.ComparerFor(false));
    }

    [TestMethod]
    public void LoneSurrogatesFoldToThemselves()
    {
        foreach (var s in new[] { "\ud801", "\udc28", "\udc28\ud801" })
            Assert.AreEqual(s, DataSyncLabelKey.Fold(s, true));
        Assert.AreEqual("A\ud801B", DataSyncLabelKey.Fold("a\ud801b", true), "only the letters around it fold");
        // A surrogate pair is one character: the Deseret pair folds, the halves alone do not.
        Assert.AreEqual(DataSyncLabelKey.Fold("\U00010428", true), DataSyncLabelKey.Fold("\U00010400", true));
        Assert.IsTrue(StringComparer.OrdinalIgnoreCase.Equals("\U00010428", "\U00010400"));
    }

    [TestMethod]
    public void KeysAreUpperCaseForEverydayLetters()
    {
        Assert.AreEqual("ACTION", DataSyncLabelKey.Fold("Action", true));
        Assert.AreEqual("STRAßE", DataSyncLabelKey.Fold("Straße", true), "ß has no simple upper case");
        Assert.AreEqual("", DataSyncLabelKey.Fold("", true));
        const string label = "Straße";
        Assert.AreSame(label, DataSyncLabelKey.Fold(label, false), "IgnoreCase off is the identity");
    }
}
