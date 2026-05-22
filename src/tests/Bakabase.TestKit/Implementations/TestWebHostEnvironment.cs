using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Bakabase.TestKit.Implementations;

/// <summary>
/// Minimal IWebHostEnvironment so services that transitively depend on it
/// (the compression / FfMpeg chain) can be resolved from the test container.
/// </summary>
public class TestWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Bakabase.TestKit";
    public string EnvironmentName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public string WebRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
