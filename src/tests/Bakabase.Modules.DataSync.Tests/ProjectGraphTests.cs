using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Bakabase.Modules.DataSync.Identity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests;

/// <summary>
/// The pure module's references (§1): DIRECT references only — the csproj's own items and the assembly's metadata
/// references (what the compiled IL uses). The transitive closure is never inspected: EF and ASP.NET reach every
/// project through Bakabase.Abstractions.
/// </summary>
[TestClass]
public class ProjectGraphTests
{
    private static readonly Assembly Module = typeof(DataSyncVersionVector).Assembly;

    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Bakabase.Modules.Property",
        "Bakabase.Modules.Federation", "Bakabase.InsideWorld", "Bakabase.Service", "Bakabase.Remoting",
        "Bakabase.Client", "Bakabase.Shell", "Bakabase.App",
    ];

    [TestMethod]
    public void TheModuleReferencesOnlyBakabaseAbstractions()
    {
        var project = LoadProject(ModuleProjectPath());
        var references = Items(project, "ProjectReference").Select(NormalizeReference).ToArray();
        CollectionAssert.AreEqual(new[] { "abstractions/Bakabase.Abstractions/Bakabase.Abstractions.csproj" }, references);
        Assert.IsTrue(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ModuleProjectPath())!,
            Items(project, "ProjectReference").Single()))));
    }

    [TestMethod]
    public void TheModuleHasNoPackageOrFrameworkReferences()
    {
        var project = LoadProject(ModuleProjectPath());
        Assert.AreEqual(0, Items(project, "PackageReference").Count(), "PackageReference");
        Assert.AreEqual(0, Items(project, "FrameworkReference").Count(), "FrameworkReference");
    }

    [TestMethod]
    public void OnlyTheModuleTestsSeeItsInternals()
    {
        var project = LoadProject(ModuleProjectPath());
        CollectionAssert.AreEqual(new[] { "Bakabase.Modules.DataSync.Tests" }, Items(project, "InternalsVisibleTo").ToArray());
        CollectionAssert.AreEqual(new[] { "Bakabase.Modules.DataSync.Tests" },
            Module.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ToArray());
    }

    [TestMethod]
    public void TheCompiledModuleUsesNoForbiddenAssembly()
    {
        var referenced = Module.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        foreach (var name in referenced)
        {
            Assert.IsFalse(ForbiddenAssemblyPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)),
                $"{Module.GetName().Name} references {name}.");
            // Bakabase.Abstractions is the only project reference, so no other Bakabase assembly may be used directly.
            if (name.StartsWith("Bakabase.", StringComparison.Ordinal))
                Assert.AreEqual("Bakabase.Abstractions", name, $"{Module.GetName().Name} references {name}.");
        }
    }

    [TestMethod]
    public void TheTestProjectReferencesOnlyTheModule()
    {
        var project = LoadProject(TestProjectPath());
        CollectionAssert.AreEqual(new[] { "modules/Bakabase.Modules.DataSync/Bakabase.Modules.DataSync.csproj" },
            Items(project, "ProjectReference").Select(NormalizeReference).ToArray());
    }

    private static XDocument LoadProject(string path)
    {
        Assert.IsTrue(File.Exists(path), path);
        return XDocument.Load(path);
    }

    private static IEnumerable<string> Items(XDocument project, string itemName) =>
        project.Descendants().Where(e => e.Name.LocalName == itemName)
            .Select(e => (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update") ?? "");

    /// <summary>"../../abstractions/X/X.csproj" → "abstractions/X/X.csproj", relative to src/.</summary>
    private static string NormalizeReference(string include)
    {
        var parts = include.Replace('\\', '/').Split('/').Where(p => p is not ("." or "..")).ToArray();
        return string.Join('/', parts);
    }

    private static string ModuleProjectPath() => Path.Combine(SourceRoot(), "modules", "Bakabase.Modules.DataSync",
        "Bakabase.Modules.DataSync.csproj");

    private static string TestProjectPath() => Path.Combine(SourceRoot(), "tests", "Bakabase.Modules.DataSync.Tests",
        "Bakabase.Modules.DataSync.Tests.csproj");

    // [CallerFilePath] tracks the source tree, not the test's bin directory: this file is src/tests/<project>/.
    private static string SourceRoot([CallerFilePath] string? callerFile = null) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", ".."));
}
