using System.Linq;
using Bakabase.Modules.ThirdParty.Helpers;
using CsQuery;

namespace Bakabase.Tests;

[TestClass]
public class HtmlContentSnapshotTests
{
    // A scrubbed form fragment reproduces the production readContent failure without
    // any site, account, post content, purchase URL or credentials.
    [TestMethod]
    [DataRow("button")]
    [DataRow("text")]
    [DataRow("password")]
    [DataRow("hidden")]
    [DataRow("checkbox")]
    [DataRow("radio")]
    [DataRow("custom&literal")]
    public void FormInputsPreserveTheirTypeAndFieldsWithoutChangingTheSourceDom(string inputType)
    {
        var document = new CQ($"""
            <!doctype html><html><head><title>Scrubbed snapshot fixture</title></head><body>
            <section id="content"><form action="?a=1&amp;amp;b=2">
            <input type="{inputType}" name="field&amp;amp;name" value="A &amp;amp; B &quot;quoted&quot; &lt; C &gt;" data-label="&amp;copy;" disabled="disabled">
            <button type="submit" data-label="send &amp;amp; keep">Continue</button>
            </form><p>Visible &amp;amp; text &lt;literal&gt;</p></section>
            </body></html>
            """);
        var content = document["#content"];
        var original = document.Render();
        var inputAttributes = content.Find("input")[0].Attributes.ToDictionary(a => a.Key, a => a.Value);
        var originalText = content.Text();

        var captured = HtmlContentSnapshot.Capture(content);
        var reparsed = new CQ(captured);

        Assert.AreEqual(original, document.Render(), "Capturing must not modify any source DOM fields.");
        Assert.AreEqual(originalText, reparsed.Text());
        var input = reparsed["input"];
        Assert.AreEqual(inputType, input.Attr("type"));
        Assert.AreEqual(inputAttributes.Count, input[0].Attributes.Count());
        foreach (var attribute in inputAttributes)
            Assert.AreEqual(attribute.Value, input.Attr(attribute.Key), $"Input attribute {attribute.Key} changed.");
        Assert.AreEqual(content.Find("form").Attr("action"), reparsed["form"].Attr("action"));
        Assert.AreEqual(content.Find("button").Attr("data-label"), reparsed["button"].Attr("data-label"));
    }

    [TestMethod]
    public void RepeatedSnapshotsDoNotDecodeLiteralEntitiesInTextOrAttributes()
    {
        var initial = new CQ("<div id='content'><a href='?value=&amp;amp;copy;&amp;x=&quot;literal&quot;'>Keep &amp;amp;copy; &lt;literal&gt;</a></div>")["#content"];
        var captured = HtmlContentSnapshot.Capture(initial);

        for (var round = 0; round < 3; round++)
        {
            var wrapper = new CQ("<div id='copy'></div>");
            wrapper["#copy"].Html(captured);
            Assert.AreEqual(initial.Text(), wrapper["#copy"].Text());
            Assert.AreEqual(initial.Find("a").Attr("href"), wrapper["a"].Attr("href"));
            captured = HtmlContentSnapshot.Capture(wrapper["#copy"]);
        }
    }

    [TestMethod]
    public void SnapshotDoesNotNormalizeBooleanFormFieldsOrMutuallyExclusiveInputGroups()
    {
        var document = new CQ("""
            <!doctype html><html><body><div id="content">
            <input type="radio" name="choice" value="first &amp;amp; literal" checked="checked">
            <input type="radio" name="choice" value="second &amp;amp; literal" checked="checked">
            <input type="checkbox" name="option" readonly="readonly" disabled="disabled">
            <button type="custom&amp;literal" name="send" value="Keep &amp;amp; literal">Continue</button>
            </div></body></html>
            """);
        var content = document["#content"];
        var original = document.Render();
        var expected = content.Find("input,button").Select(n => n.Attributes.ToDictionary(a => a.Key, a => a.Value)).ToArray();

        var captured = new CQ(HtmlContentSnapshot.Capture(content));

        Assert.AreEqual(original, document.Render());
        var actual = captured["input,button"].ToArray();
        Assert.AreEqual(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i].Count, actual[i].Attributes.Count());
            foreach (var attribute in expected[i])
                Assert.AreEqual(attribute.Value, actual[i].GetAttribute(attribute.Key), $"Field {i}: {attribute.Key} changed.");
        }
    }
}
