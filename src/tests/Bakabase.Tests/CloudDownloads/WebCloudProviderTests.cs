using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.TestKit.Implementations;
using Bakabase.Modules.Downloader.Cloud;
using System.Text.Json;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class WebCloudProviderTests
{
    [TestMethod]
    public void Process_arguments_are_passed_without_a_shell_and_ignore_ambient_python_paths()
    {
        var arguments = new[] { "-I", "-u", "a directory/script with spaces.py", "$(must-not-execute)" };
        var start = CloudToolProcess.StartInfo("private-python", arguments, Path.GetTempPath());
        Assert.IsFalse(start.UseShellExecute);
        Assert.IsTrue(start.RedirectStandardInput);
        CollectionAssert.AreEqual(arguments, start.ArgumentList.ToArray());
        Assert.IsFalse(start.Environment.ContainsKey("PYTHONPATH"));
        Assert.IsFalse(start.Environment.ContainsKey("PYTHONHOME"));
    }

    [TestMethod]
    public void Provider_rejects_other_hosts_and_credential_bearing_urls()
    {
        foreach (var url in new[] { "https://gofile.io.attacker.test/d/1", "http://gofile.io/d/1", "https://user:secret@gofile.io/d/1", "https://gofile.io:8080/d/1" })
            Assert.ThrowsException<ArgumentException>(() => WebCloudProvider.ValidateShareUrl("gofile", url));
        Assert.ThrowsException<ArgumentException>(() => WebCloudProvider.ValidateShareUrl("googleDrive", "https://example.com/file.zip"));
    }

    [TestMethod]
    public void Explicit_proxy_uses_the_existing_per_source_resolver_and_keeps_credentials_out_of_the_url()
    {
        var options = new NetworkOptions
        {
            Proxy = new() {Mode = NetworkOptions.ProxyMode.UseCustom, CustomProxyId = "global"},
            CustomProxies =
            [
                new() {Id = "global", Address = "http://global-proxy.invalid:8080"},
                new() {Id = "google", Address = "socks5://127.0.0.1:1080", Credentials = new()
                    {Username = "alice@example.test", Password = "p:a@ss", Domain = "WORK"}}
            ],
            ThirdPartyProxies = new()
            {
                [(int)ThirdPartyId.GoogleDrive] = new() {Mode = NetworkOptions.ProxyMode.UseCustom, CustomProxyId = "google"},
                [(int)ThirdPartyId.Gofile] = new() {Mode = NetworkOptions.ProxyMode.DoNotUse}
            }
        };
        var resolver = new BakabaseWebProxy(new TestBOptions<NetworkOptions>(options));
        var selected = CloudToolProxy.Resolve(resolver.ForThirdParty(ThirdPartyId.GoogleDrive), new Uri("https://drive.google.com/"));
        Assert.IsNotNull(selected);
        Assert.AreEqual("socks5://127.0.0.1:1080", selected.Url);
        Assert.AreEqual("WORK\\alice@example.test", selected.Username);
        Assert.AreEqual("p:a@ss", selected.Password);
        Assert.IsFalse(selected.Url.Contains("alice", StringComparison.Ordinal));
        Assert.IsFalse(selected.ToString().Contains("p:a@ss", StringComparison.Ordinal));
        Assert.IsNull(CloudToolProxy.Resolve(resolver.ForThirdParty(ThirdPartyId.Gofile), new Uri("https://gofile.io/")));
    }

    [TestMethod]
    public void Python_stdin_contract_serializes_nested_proxy_fields_as_camel_case()
    {
        var source = new CloudSource {Id = "source", ProviderId = "googleDrive", Url = "https://drive.google.com/file/d/fixture/view"};
        var request = new CloudDownloadRequest("operation", source, null, null, Path.GetTempPath());
        var settings = new CloudToolProxy.Settings("http://proxy.invalid:8080", "user", "private-password");
        using var json = JsonDocument.Parse(WebCloudProvider.SerializeRequest("googleDrive", request, Path.GetTempPath(), settings));
        var proxy = json.RootElement.GetProperty("proxy");
        Assert.AreEqual(settings.Url, proxy.GetProperty("url").GetString());
        Assert.AreEqual(settings.Username, proxy.GetProperty("username").GetString());
        Assert.AreEqual(settings.Password, proxy.GetProperty("password").GetString());
        Assert.IsFalse(proxy.TryGetProperty("Url", out _));
    }

    [TestMethod]
    public void Child_proxy_environment_clears_ambient_values_when_the_application_selects_direct()
    {
        var process = CloudToolProcess.StartInfo("go", ["build"], Path.GetTempPath());
        process.Environment["HTTPS_PROXY"] = "http://ambient.invalid:8080";
        process.Environment["http_proxy"] = "http://ambient.invalid:8080";
        process.Environment["NO_PROXY"] = "*";
        var resolver = new BakabaseWebProxy(new TestBOptions<NetworkOptions>(new()));
        CloudToolProxy.ApplyToEnvironment(process, resolver, new Uri("https://proxy.golang.org/"));
        Assert.IsFalse(process.Environment.ContainsKey("HTTPS_PROXY"));
        Assert.IsFalse(process.Environment.ContainsKey("http_proxy"));
        Assert.IsFalse(process.Environment.ContainsKey("NO_PROXY"));
        CollectionAssert.AreEqual(new[] {"build"}, process.ArgumentList.ToArray());
    }

    [TestMethod]
    public void Final_manifest_cannot_escape_destination_by_sibling_prefix_or_traversal()
    {
        var directory = Directory.CreateTempSubdirectory("cloud-manifest-");
        try
        {
            var output = Path.Combine(directory.FullName, "download");
            var other = Path.Combine(directory.FullName, "download-other");
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(other);
            var file = Path.Combine(other, "file.zip");
            File.WriteAllText(file, "existing");
            Assert.ThrowsException<InvalidDataException>(() => WebCloudProvider.ValidateResultFiles(output, [file]));
            Assert.ThrowsException<InvalidDataException>(() => WebCloudProvider.ValidateResultFiles(output, [Path.Combine(output, "..", "download-other", "file.zip")]));
            Assert.ThrowsException<InvalidDataException>(() => WebCloudProvider.ValidateResultFiles(output, []));
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public void Final_manifest_must_have_existing_unique_files()
    {
        var directory = Directory.CreateTempSubdirectory("cloud-manifest-");
        try
        {
            var file = Path.Combine(directory.FullName, "file.zip");
            File.WriteAllText(file, "complete");
            WebCloudProvider.ValidateResultFiles(directory.FullName, [file]);
            Assert.ThrowsException<InvalidDataException>(() => WebCloudProvider.ValidateResultFiles(directory.FullName, [file, file]));
            Assert.ThrowsException<InvalidDataException>(() => WebCloudProvider.ValidateResultFiles(directory.FullName, [Path.Combine(directory.FullName, "missing.zip")]));
        }
        finally { directory.Delete(recursive: true); }
    }
}
