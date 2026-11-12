using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Dependency.Exceptions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class ZipPasswordCodePageTests
{
    private string _root = null!;
    private const string Password = "known-密码";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [TestInitialize] public void Setup() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "zip-password-" + Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public void PasswordEncodingIsExplicitDistinctAndPreservesLegacyNullIdentities()
    {
        var original = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive"}]};
        var legacy = FileProcessingPlanExecutor.IdentityJson(original);
        Assert.IsFalse(legacy.Contains("zipPasswordCodePage"));
        Assert.IsFalse(Newtonsoft.Json.JsonConvert.SerializeObject(original).Contains("ZipPasswordCodePage"));
        var configured = original with {Steps = [original.Steps[0] with {ZipPasswordCodePage = ZipPasswordCodePage.SimplifiedChineseGbk}]};
        Assert.AreNotEqual(FileProcessingPlanExecutor.CheckpointIdentity(original, ["a.zip"]), FileProcessingPlanExecutor.CheckpointIdentity(configured, ["a.zip"]));
        var intent = FileArtifactProcessor.Intent("source", legacy, Password, new());
        Assert.AreNotEqual(intent, FileArtifactProcessor.Intent("source", JsonSerializer.Serialize(configured, Json), Password, new()));
        Assert.AreNotEqual(FileArtifactProcessor.Intent("source", JsonSerializer.Serialize(configured, Json), Password, new()),
            FileArtifactProcessor.Intent("source", JsonSerializer.Serialize(configured with {Steps = [configured.Steps[0] with {ZipPasswordCodePage = ZipPasswordCodePage.Utf8}]}, Json), Password, new()));
        Assert.AreEqual(ZipPasswordCodePage.SimplifiedChineseGbk, JsonSerializer.Deserialize<FileProcessingPlan>(JsonSerializer.Serialize(configured, Json), Json)!.Steps[0].ZipPasswordCodePage);
        var nameOnly = ArchiveOpenOptions.WithZipFileNameCodePage(null, ZipFileNameCodePage.JapaneseShiftJis);
        var both = ArchiveOpenOptions.WithZipPasswordCodePage(nameOnly, ZipPasswordCodePage.SimplifiedChineseGbk)!;
        Assert.AreEqual(ZipFileNameCodePage.JapaneseShiftJis, both.ZipFileNameCodePage);
        Assert.AreEqual(ZipPasswordCodePage.SimplifiedChineseGbk, both.ZipPasswordCodePage);
        Assert.IsNull(nameOnly!.ZipPasswordCodePage);
        Assert.ThrowsExactly<ArgumentException>(() => ArchiveOpenOptions.WithZipPasswordCodePage(ArchiveOpenOptions.Parser(), ZipPasswordCodePage.SimplifiedChineseGbk));
        Assert.ThrowsExactly<InvalidOperationException>(() => FileProcessingPlanValidator.Validate(configured with {Steps = [configured.Steps[0] with {Op = "moveFile", TargetDirectory = "."}]}));
        Assert.ThrowsExactly<InvalidOperationException>(() => FileProcessingPlanValidator.Validate(configured with {Steps = [configured.Steps[0] with {ZipPasswordCodePage = (ZipPasswordCodePage)123}]}));
    }

    [DataTestMethod]
    [DataRow(128, 0, 1)] [DataRow(192, 0, 2)] [DataRow(256, 0, 2)]
    [DataRow(128, 8, 2)] [DataRow(192, 8, 1)] [DataRow(256, 8, 2)]
    public async Task MatureReaderAuthenticatesExactGbkPasswordAndNeverChangesSource(int bits, int method, int aesVersion)
    {
        var archive = await Fixture(bits: bits, method: method, version: aesVersion);
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(archive));
        var service = Service();
        var wrongEncoding = await service.TestCompressedFile(archive, Password, null, null, null, default, Options(ZipPasswordCodePage.Utf8));
        Assert.AreEqual(2, wrongEncoding.ExitCode);
        var probe = await new ArchiveExtractionService(service).ProbePasswordAsync(archive, ["wrong", Password], null, default, Options());
        Assert.IsTrue(probe.Succeeded, probe.Message); Assert.AreEqual(Password, probe.Password);
        CollectionAssert.Contains(probe.WrongPasswords.ToArray(), "wrong");
        var output = Path.Combine(_root, "output");
        var result = await service.ExtractWithProgress(archive, output, Password, true, CompressedFileService.OverwriteMode.None,
            CompressedFileService.ProgressOutputTarget.StandardOutput, null, null, null, default, Options());
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("authenticated payload", await File.ReadAllTextAsync(Path.Combine(output, "中文", "key.txt")));
        CollectionAssert.AreEqual(hash, SHA256.HashData(await File.ReadAllBytesAsync(archive)));
        // The public adapter never overwrites a user's existing destination, even when requested.
        var error = await Assert.ThrowsExactlyAsync<ArchivePasswordEncodingException>(() => service.ExtractWithProgress(archive, output, Password, true,
            CompressedFileService.OverwriteMode.OverwriteAll, CompressedFileService.ProgressOutputTarget.StandardOutput, null, null, null, default, Options()));
        Assert.AreEqual("ArchiveUnsafeOutput", error.Code);
    }

    [DataTestMethod]
    [DataRow("hmac")] [DataRow("ciphertext")] [DataRow("size")] [DataRow("crc")]
    [DataRow("local-encryption")] [DataRow("local-method")] [DataRow("local-size")]
    [DataRow("prefix")] [DataRow("truncated")]
    public async Task CorruptOrUnsupportedInputNeverCreatesOutput(string corruption)
    {
        var archive = await Fixture(corruption: corruption, version: corruption == "crc" ? 1 : 2);
        var output = Path.Combine(_root, "rejected");
        var error = await Assert.ThrowsExactlyAsync<ArchivePasswordEncodingException>(() => Extract(archive, output));
        Assert.IsTrue(error.Code is "ArchiveIntegrityFailure" or "ArchivePasswordEncodingUnsupported", error.Code);
        Assert.IsFalse(Directory.Exists(output)); Assert.IsTrue(File.Exists(archive));
    }

    [DataTestMethod]
    [DataRow("../outside.txt")] [DataRow("/outside.txt")] [DataRow("CON.txt")] [DataRow("CONOUT$")]
    [DataRow("collision")] [DataRow("parent-collision")] [DataRow("symlink")]
    [DataRow("depth")] [DataRow("unicode-length")]
    public async Task UnsafeNamesAndLinksAreRejectedBeforeOutput(string name)
    {
        var archive = await Fixture(unsafeName: name, legacyNames: name == "unicode-length");
        var output = Path.Combine(_root, "unsafe");
        var error = await Assert.ThrowsExactlyAsync<ArchivePasswordEncodingException>(() => Extract(archive, output));
        Assert.AreEqual("ArchiveUnsafeOutput", error.Code); Assert.IsFalse(Directory.Exists(output));
    }

    [TestMethod]
    public async Task ThirtyTwoPathComponentsRemainSupported()
    {
        var name = string.Concat(Enumerable.Repeat("a/", 31)) + "key.txt";
        var archive = await Fixture(unsafeName: name);
        var output = Path.Combine(_root, "bounded-depth");
        await Extract(archive, output);
        Assert.AreEqual("authenticated payload", await File.ReadAllTextAsync(Path.Combine(output, name.Replace('/', Path.DirectorySeparatorChar))));
    }

    [TestMethod]
    public async Task UnicodePathExtraAndSeparateLegacyNameEncodingAreRespected()
    {
        var archive = await Fixture(legacyNames: true);
        var output = Path.Combine(_root, "legacy");
        var result = await Extract(archive, output);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("authenticated payload", await File.ReadAllTextAsync(Path.Combine(output, "中文", "key.txt")));
        var names = await Service().GetCompressFileEntries(archive, default, Options());
        Assert.AreEqual("中文/key.txt", names.Data!.Single().Path);
        // A verified Unicode Path extra takes precedence even when the fallback cannot decode raw bytes.
        names = await Service().GetCompressFileEntries(archive, default,
            ArchiveOpenOptions.WithZipFileNameCodePage(Options(), ZipFileNameCodePage.Utf8));
        Assert.AreEqual("中文/key.txt", names.Data!.Single().Path);
    }

    [TestMethod]
    public async Task PlainCompanionsAndEmptyDirectoriesRemainInAuthenticatedOutput()
    {
        var archive = await Fixture(companions: true);
        var output = Path.Combine(_root, "companions");
        await Extract(archive, output);
        Assert.AreEqual("keep this key", await File.ReadAllTextAsync(Path.Combine(output, "key.txt")));
        Assert.IsTrue(Directory.Exists(Path.Combine(output, "empty")));
        Assert.AreEqual(2, Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Count());
    }

    [TestMethod]
    public async Task ExpansionLimitIsCheckedBeforeDecryptingOrCreatingOutput()
    {
        var archive = await Fixture(corruption: "expanded-total");
        var output = Path.Combine(_root, "limit");
        var error = await Assert.ThrowsExactlyAsync<ArchivePasswordEncodingException>(() => Extract(archive, output));
        Assert.AreEqual("ArchiveExpansionLimit", error.Code);
        Assert.IsFalse(Directory.Exists(output));
    }

    [TestMethod]
    public async Task UnrepresentablePasswordIsNotSilentlyReplaced()
    {
        var archive = await Fixture();
        var result = await Assert.ThrowsExactlyAsync<ArchivePasswordEncodingException>(() => Service().TestCompressedFile(archive, "🦊", null, null, null, default, Options()));
        Assert.AreEqual("ArchivePasswordNotRepresentable", result.Code);
    }

    [TestMethod]
    public async Task MissingDependencySuspendsCurrentStepWithoutPasswordGuessOrSourceMutation()
    {
        var archive = Path.Combine(_root, "input.zip"); await File.WriteAllBytesAsync(archive, "PK\u0003\u0004fixture"u8.ToArray());
        var tool = new MissingDependency();
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive", Password = Password,
            ZipPasswordCodePage = ZipPasswordCodePage.SimplifiedChineseGbk}]};
        var result = await new FileProcessingPlanExecutor(tool).ExecuteAsync(plan, _root, [archive], Path.Combine(_root, "state"));
        Assert.IsFalse(result.Completed); Assert.IsFalse(result.NeedsPassword); Assert.AreEqual("DEPENDENCY_REQUIRED", result.FailureCode);
        Assert.IsTrue(File.Exists(archive)); Assert.IsTrue(FileProcessingResumeActions.CanRetry(result.FailureCode));
    }

    [TestMethod]
    public async Task CancellationKillsOnlyAdapterAndCannotReturnSuccess()
    {
        var start = Runtime().StartInfo(["-I", "-c", "import sys,time; sys.stdin.read(); print('{\"progress\":1}',flush=True); time.sleep(60)"], _root);
        using var cancellation = new CancellationTokenSource();
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() => ZipPasswordCompatibility.RunProcessAsync(start, "{}", _ => cancellation.Cancel(), cancellation.Token));
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    private Task<CompressedFileService.TestResult> Extract(string archive, string output) => Service().ExtractWithProgress(archive, output, Password,
        true, CompressedFileService.OverwriteMode.None, CompressedFileService.ProgressOutputTarget.StandardOutput, null, null, null, default, Options());
    private static ArchiveOpenOptions Options(ZipPasswordCodePage code = ZipPasswordCodePage.SimplifiedChineseGbk) => ArchiveOpenOptions.WithZipPasswordCodePage(null, code)!;
    private static PythonRuntimeExecution Runtime()
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_PYZIPPER_PYTHON");
        if (string.IsNullOrEmpty(executable) || !File.Exists(executable)) Assert.Inconclusive("Set BAKABASE_TEST_PYZIPPER_PYTHON to an isolated pyzipper 0.4.0 environment.");
        return new(new() {Mode = PythonRuntimeMode.External, PythonExecutable = executable}, new("test", "test", executable!, "fixture"));
    }
    private static CompressedFileService Service() => new CompatibilityService();
    private sealed class CompatibilityService() : CompressedFileService(null!, NullLogger<CompressedFileService>.Instance)
    {
        protected override Task<PythonRuntimeExecution> GetZipCompatibilityExecutionAsync(CancellationToken ct) => Task.FromResult(Runtime());
    }
    private sealed class MissingDependency : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> passwords, Action<string>? progress, CancellationToken ct) => throw new DependencyNotInstalledException("Pyzipper", "pyzipper", "Install pyzipper.");
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> passwords, Action<string>? progress, CancellationToken ct, ArchiveOpenOptions? options) => ProbePasswordAsync(entry, passwords, progress, ct);
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct) => throw new AssertFailedException("Extraction must not start without dependency.");
    }
    private async Task<string> Fixture(int bits = 256, int method = 8, int version = 2, string? corruption = null,
        string? unsafeName = null, bool legacyNames = false, bool companions = false)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        const string script = """
import sys,json,pyzipper,struct,binascii
r=json.load(sys.stdin)
class LegacyInfo(pyzipper.zipfile_aes.AESZipInfo):
 def _encodeFilenameFlags(self):
  raw=self.filename.encode('gbk');return raw,self.flag_bits & ~0x800
 def encode_local_header(self,*,extra,**kwargs):
  return super().encode_local_header(extra=extra+self.extra,**kwargs)
 def encode_central_directory(self,*,extra_data,**kwargs):
  return super().encode_central_directory(extra_data=extra_data+self.extra,**kwargs)
class LegacyZip(pyzipper.AESZipFile):zipinfo_cls=LegacyInfo
cls=LegacyZip if r['legacyNames'] else pyzipper.AESZipFile
kind=r['unsafeName'];names=['中文/key.txt']
if r['corruption']=='expanded-total':names=['part-%s.txt'%i for i in range(17)]
if kind=='collision':names=['same.txt','SAME.txt']
elif kind=='parent-collision':names=['Folder/a.txt','folder/b.txt']
elif kind=='symlink':names=['linked.txt']
elif kind=='depth':names=['a/'*32+'key.txt']
elif kind=='unicode-length':names=['中'*342+'.txt']
elif kind:names=[kind]
with cls(r['path'],'w',compression=r['method'],encryption=pyzipper.WZ_AES) as z:
 z.setpassword(r['password'].encode('gbk'));z.setencryption(pyzipper.WZ_AES,nbits=r['bits'],force_wz_aes_version=r['version'])
 for name in names:
  i=z.zipinfo_cls(name);i.compress_type=r['method']
  if kind=='symlink':i.external_attr=0xa1ff0000
  if r['legacyNames']:
   raw=name.encode('gbk');u=name.encode('utf8');i.extra=struct.pack('<HHBI',0x7075,len(u)+5,1,binascii.crc32(raw))+u
  z.writestr(i,b'authenticated payload')
if r['companions']:
 with pyzipper.AESZipFile(r['path'],'a',compression=8) as z:
  z.writestr('key.txt',b'keep this key');z.writestr('empty/',b'')
b=bytearray(open(r['path'],'rb').read());c=b.index(b'PK\x01\x02');n,e=struct.unpack_from('<HH',b,26);data=30+n+e;packed=struct.unpack_from('<I',b,c+20)[0];what=r['corruption']
if what=='hmac':b[data+packed-1]^=1
elif what=='ciphertext':b[data+r['bits']//16+2]^=1
elif what=='size':struct.pack_into('<I',b,22,3);struct.pack_into('<I',b,c+24,3)
elif what=='crc':struct.pack_into('<I',b,14,1);struct.pack_into('<I',b,c+16,1)
elif what=='local-encryption':b[6]^=1
elif what=='local-method':b[8]=0
elif what=='local-size':struct.pack_into('<I',b,22,1)
elif what=='prefix':b=b'MZprefix'+b
elif what=='truncated':b=b[:-10]
elif what=='expanded-total':
 p=c
 while b[p:p+4]==b'PK\x01\x02':
  local=struct.unpack_from('<I',b,p+42)[0];struct.pack_into('<I',b,local+22,0xfffffffe);struct.pack_into('<I',b,p+24,0xfffffffe)
  ns,es,cs=struct.unpack_from('<HHH',b,p+28);p+=46+ns+es+cs
open(r['path'],'wb').write(b)
""";
        using var process = new Process {StartInfo = Runtime().StartInfo(["-I", "-c", script], _root)};
        process.Start();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new {path, password = Password, bits, method, version, corruption, unsafeName, legacyNames, companions}));
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); await output;
        Assert.AreEqual(0, process.ExitCode, await error);
        return path;
    }
}
