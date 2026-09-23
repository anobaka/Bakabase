using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Bakabase.Client.Remoting.Components.Forwarding;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// The connect page's two dictionaries, kept in step with each other and with the markup.
/// </summary>
/// <remarks>
/// <para>
/// This page is the first thing a user of the thin client sees, and for a long time it was
/// the only screen in the product that had never been translated — it carries its own
/// strings because it is shown before there is a server, and the app's translations live
/// on the server.
/// </para>
/// <para>
/// Which is exactly the arrangement that rots: there is no translation tooling pointed at
/// this file, so a string added in English is simply absent in Chinese, and absent quietly
/// — the lookup falls back to English and the page still renders. These read the shipped
/// page back and compare the two dictionaries against each other and against every key the
/// markup asks for.
/// </para>
/// </remarks>
[TestClass]
public class ConnectPageTests
{
    /// <summary>
    /// The keys of one language's block. Read off the page itself rather than duplicated
    /// here — a copy would be one more thing to forget.
    /// </summary>
    private static HashSet<string> Dictionary(string language)
    {
        var opening = $"\n    {language}: {{";
        var start = ConnectPage.Html.IndexOf(opening, System.StringComparison.Ordinal);

        Assert.IsTrue(start >= 0, $"No '{language}' dictionary in the connect page.");

        var body = ConnectPage.Html[(start + opening.Length)..];
        var end = body.IndexOf("\n    }", System.StringComparison.Ordinal);

        Assert.IsTrue(end >= 0, $"The '{language}' dictionary is not closed as expected.");

        return Regex.Matches(body[..end], @"^\s*'(?<key>[\w.]+)':", RegexOptions.Multiline)
            .Select(m => m.Groups["key"].Value)
            .ToHashSet();
    }

    [TestMethod]
    public void Both_languages_carry_the_same_keys()
    {
        var en = Dictionary("en");
        var zh = Dictionary("zh");

        Assert.IsTrue(en.Count > 20, $"Only {en.Count} English strings — the block was probably misread.");

        // Named rather than counted: the failure a reader needs is which string they added
        // on one side and not the other.
        CollectionAssert.AreEquivalent(en.OrderBy(k => k).ToList(), zh.OrderBy(k => k).ToList(),
            $"Only in en: {string.Join(", ", en.Except(zh).OrderBy(k => k))}. " +
            $"Only in zh: {string.Join(", ", zh.Except(en).OrderBy(k => k))}.");
    }

    [TestMethod]
    public void Every_key_the_markup_asks_for_exists()
    {
        var en = Dictionary("en");

        // data-t names one key; data-t-attr names "attribute:key" pairs, comma separated.
        var asked = Regex.Matches(ConnectPage.Html, @"data-t=""(?<key>[\w.]+)""")
            .Select(m => m.Groups["key"].Value)
            .Concat(Regex.Matches(ConnectPage.Html, @"data-t-attr=""(?<pairs>[^""]+)""")
                .SelectMany(m => m.Groups["pairs"].Value.Split(','))
                .Select(pair => pair.Split(':').Last().Trim()))
            .ToHashSet();

        Assert.IsTrue(asked.Count > 10, $"Only {asked.Count} translated elements — the markup was probably misread.");

        var missing = asked.Except(en).OrderBy(k => k).ToList();

        // An element asking for a key nobody defined renders the key itself, which reaches
        // the user as "pairing.heading" where a heading should be.
        Assert.AreEqual(0, missing.Count, $"Markup asks for undefined keys: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void Every_outcome_the_client_can_report_has_something_to_say()
    {
        // describeHandshake and handlePairing look the outcome's own enum name up in the
        // dictionary, so a member added to either C# enum needs an entry here — and
        // without one the page falls back to a raw server detail, or to "could not
        // connect", which is what every distinct outcome exists to avoid.
        var en = Dictionary("en");

        foreach (var name in System.Enum.GetNames<Bakabase.Remoting.Abstractions.Models.ServerHandshakeOutcome>()
                     .Where(n => n != nameof(Bakabase.Remoting.Abstractions.Models.ServerHandshakeOutcome.Ok)))
        {
            Assert.IsTrue(en.Contains($"handshake.{name}"), $"No connect-page text for handshake outcome {name}.");
        }

        foreach (var name in System.Enum
                     .GetNames<Bakabase.Remoting.Components.Connection.ClientPairingOutcome>()
                     .Where(n => n != nameof(Bakabase.Remoting.Components.Connection.ClientPairingOutcome
                         .Paired)))
        {
            Assert.IsTrue(en.Contains($"pairing.{name}"), $"No connect-page text for pairing outcome {name}.");
        }
    }
}
