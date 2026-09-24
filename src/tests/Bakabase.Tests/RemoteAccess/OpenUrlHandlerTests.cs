using Bakabase.Remoting.Components.UserMachine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// What the client is willing to hand to the operating system.
/// </summary>
/// <remarks>
/// The all-in-one passes the URL straight to the shell, and there that is fine: the only
/// thing that can reach the endpoint is a page the same machine is serving. Here it
/// arrives from a server that may be somebody else's, and <c>UseShellExecute</c> opens
/// whatever the string names — on Windows that includes <c>file:</c> paths and any
/// registered protocol handler, which is a program launch dressed as a link.
/// </remarks>
[TestClass]
public class OpenUrlHandlerTests
{
    [TestMethod]
    public void Web_links_are_opened()
    {
        Assert.IsTrue(OpenUrlHandler.IsOpenable("http://example.com"));
        Assert.IsTrue(OpenUrlHandler.IsOpenable("https://example.com/a?b=c#d"));
        Assert.IsTrue(OpenUrlHandler.IsOpenable("https://192.168.1.5:34567/"));
    }

    [TestMethod]
    public void A_local_file_is_not_a_link()
    {
        Assert.IsFalse(OpenUrlHandler.IsOpenable("file:///etc/passwd"));
        Assert.IsFalse(OpenUrlHandler.IsOpenable(@"file://C:/Windows/System32/calc.exe"));
    }

    [TestMethod]
    public void A_registered_protocol_handler_is_a_program_launch()
    {
        // Each of these asks the OS to start something. The frontend has no reason to
        // send one, and a server that does is not one to indulge.
        foreach (var url in new[]
                 {
                     "steam://rungameid/440", "ms-settings:privacy", "vscode://file/etc/passwd",
                     "javascript:alert(1)", "data:text/html,<script>alert(1)</script>"
                 })
        {
            Assert.IsFalse(OpenUrlHandler.IsOpenable(url), url);
        }
    }

    [TestMethod]
    public void Nothing_that_is_not_a_url_gets_through()
    {
        // A bare string reaches ShellExecute as a path or a command, which is exactly
        // what must not happen.
        foreach (var url in new[] {null, "", "   ", "calc.exe", "/usr/bin/xdg-open", @"C:\Windows\System32\calc.exe"})
        {
            Assert.IsFalse(OpenUrlHandler.IsOpenable(url), url ?? "null");
        }
    }
}
