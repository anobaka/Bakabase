using Bakabase.Modules.Player.Components;
using FluentAssertions;

namespace Bakabase.Modules.Player.Tests;

[TestClass]
public class BatchPlayArgumentsTests
{
    [TestMethod]
    public void BuildFromTemplate_NullTemplate_QuotesTarget()
    {
        BatchPlayArguments.BuildFromTemplate(null, @"C:\a b\v.mp4")
            .Should().Be("\"C:\\a b\\v.mp4\"");
    }

    [TestMethod]
    public void BuildFromTemplate_PlainPlaceholder_QuotesTarget()
    {
        BatchPlayArguments.BuildFromTemplate("/fullscreen {0}", @"C:\v.mp4")
            .Should().Be("/fullscreen \"C:\\v.mp4\"");
    }

    [TestMethod]
    public void BuildFromTemplate_AlreadyQuotedPlaceholder_DoesNotDoubleQuote()
    {
        BatchPlayArguments.BuildFromTemplate("\"{0}\" --loop", @"C:\v.mp4")
            .Should().Be("\"C:\\v.mp4\" --loop");
    }

    [TestMethod]
    public void BuildFromTemplate_EscapesQuotesInTarget()
    {
        BatchPlayArguments.BuildFromTemplate("{0}", "C:\\a\"b.mp4")
            .Should().Be("\"C:\\a\\\"b.mp4\"");
    }

    [TestMethod]
    public void BuildMultiFile_QuotesAndJoinsWithSpaces()
    {
        BatchPlayArguments.BuildMultiFile([@"C:\a.mp4", @"C:\dir with space\b.mkv"])
            .Should().Be("\"C:\\a.mp4\" \"C:\\dir with space\\b.mkv\"");
    }

    [TestMethod]
    public void BuildMultiFile_EmptyInput_ReturnsEmptyString()
    {
        BatchPlayArguments.BuildMultiFile([]).Should().BeEmpty();
    }
}
