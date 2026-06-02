using Bakabase.Modules.Enhancer.Components.Enhancers.Av;
using FluentAssertions;

namespace Bakabase.Modules.Enhancer.Tests;

/// <summary>
/// Pins the mapping of the AV enhancer's raw mosaic string to a "has mosaic"
/// (censored) boolean. The bug this guards against: the Mosaic target used to be
/// declared as a plain string, so binding it to a Boolean property routed the
/// value through the generic string→boolean conversion — which treats any
/// non-empty, non-"0"/"false" string as <c>true</c>. That made every result
/// (有码 AND 无码 AND 国产 …) come out as "有"/true.
///
/// **Contract:** uncensored markers → false, censored markers → true, and any
/// value that does not actually describe the mosaic state (国产/同人/里番/empty)
/// → null, so the enhancer never asserts an arbitrary boolean.
/// </summary>
[TestClass]
public sealed class AvMosaicParsingTests
{
    [DataTestMethod]
    [DataRow("无码")]
    [DataRow("無碼")]
    [DataRow("無修正")]
    [DataRow("uncensored")]
    [DataRow("Uncensored")]
    [DataRow(" 无码 ")]
    public void Uncensored_ReturnsFalse(string raw)
    {
        AvEnhancer.ParseMosaic(raw).Should().BeFalse();
    }

    [DataTestMethod]
    [DataRow("有码")]
    [DataRow("有碼")]
    [DataRow(" 有码 ")]
    public void Censored_ReturnsTrue(string raw)
    {
        AvEnhancer.ParseMosaic(raw).Should().BeTrue();
    }

    [DataTestMethod]
    [DataRow("国产")]
    [DataRow("同人")]
    [DataRow("里番")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void NonMosaicOrUnknown_ReturnsNull(string? raw)
    {
        AvEnhancer.ParseMosaic(raw).Should().BeNull();
    }
}
