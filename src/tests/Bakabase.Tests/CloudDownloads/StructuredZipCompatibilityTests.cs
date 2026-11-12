using System.Diagnostics;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Dependency.Exceptions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class StructuredZipCompatibilityTests
{
    private string _root = null!;
    [TestInitialize] public void Setup() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "structured-zip-"+Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() => Directory.Delete(_root,true);

    [DataTestMethod]
    [DataRow(536870912L, false, 0)]
    [DataRow(3221225472L, true, 20494)]
    [DataRow(6442450944L, true, 32768)]
    public async Task ExplicitExtractionSeeksPastSparseMediaAndRetainsAllCompanions(long prefix, bool zip64, int trailing)
    {
        var file = await Fixture(prefix,zip64,trailing);
        var sourceSize = new FileInfo(file).Length;
        var service = new Service();
        var format = await service.ProbeAuthorizedArchiveFormatAsync(file,default);
        Assert.AreEqual(".zip",format?.Extension);
        Assert.AreEqual(prefix,format!.OpenOptions!.ZipStructure!.ZipOffset);
        Assert.IsNull(format.OpenOptions.TypeArgument); // No 7-Zip scanning switch is created.
        var entries = await service.GetCompressFileEntries(file,default,format.OpenOptions);
        Assert.AreEqual(2,entries.Data!.Count);
        var tested = await service.TestCompressedFile(file,null,null,null,null,default,format.OpenOptions);
        Assert.AreEqual(0,tested.ExitCode);
        var output = Path.Combine(_root,"output");
        var extracted = await Extract(service,file,output,format.OpenOptions);
        Assert.AreEqual(0,extracted.ExitCode);
        Assert.AreEqual("inert payload",await File.ReadAllTextAsync(Path.Combine(output,"package","payload.txt")));
        Assert.AreEqual("preserve key",await File.ReadAllTextAsync(Path.Combine(output,"key.txt")));
        Assert.AreEqual(sourceSize,new FileInfo(file).Length);
        Assert.AreEqual(1,Directory.GetFiles(_root).Length); // Never materialize a stripped ZIP copy.
        Assert.AreEqual(2,Directory.GetFiles(output,"*",SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task AutomaticAndRecursiveMediaDiscoveryNeverRequestsTheCompatibilityReader()
    {
        var file = await Fixture(1024L*1024*1024,true,16000);
        var service = new Service();
        var archives = new ArchiveExtractionService(service);
        Assert.AreEqual(0,(await AuthorizedArchiveDetection.DetectAsync([file],false,default,archives)).Count);
        Assert.AreEqual(0,(await AuthorizedArchiveDetection.DetectAsync([file],true,default,archives,
            exactExtractionSelection:true)).Count);
        Assert.AreEqual(0,service.CompatibilityStarts);
    }

    [TestMethod]
    public async Task VerifiedZip64WindowAuthenticatesExplicitPasswordBytesWithoutTouchingThePrefix()
    {
        const string password="known-密码";
        var file=await Fixture(1024L*1024*1024,true,16000,encrypted:true);
        var service=new Service();
        var opening=(await service.ProbeAuthorizedArchiveFormatAsync(file,default))!.OpenOptions!;
        var rejected=await service.TestCompressedFile(file,password,null,null,null,default,opening);
        Assert.AreEqual(2,rejected.ExitCode);
        opening=ArchiveOpenOptions.WithZipPasswordCodePage(opening,ZipPasswordCodePage.SimplifiedChineseGbk)!;
        var output=Path.Combine(_root,"encrypted-output");
        var extracted=await service.ExtractWithProgress(file,output,password,true,CompressedFileService.OverwriteMode.None,
            CompressedFileService.ProgressOutputTarget.StandardOutput,null,null,null,default,opening);
        Assert.AreEqual(0,extracted.ExitCode);
        Assert.AreEqual("preserve key",await File.ReadAllTextAsync(Path.Combine(output,"key.txt")));
        Assert.IsTrue(File.Exists(file));
    }

    [DataTestMethod]
    [DataRow("tail")]
    [DataRow("central")]
    [DataRow("local")]
    [DataRow("payload")]
    public async Task ChangedStructureOrCorruptPayloadNeverPublishesOutput(string damage)
    {
        var file = await Fixture(1024L*1024*1024,true,16000);
        var service = new Service();
        var opening = (await service.ProbeAuthorizedArchiveFormatAsync(file,default))!.OpenOptions!;
        var proof = opening.ZipStructure!;
        var offset = damage switch
        {
            "tail" => proof.SourceLength-1,
            "central" => proof.CentralDirectoryOffset+16,
            "local" => proof.ZipOffset+30,
            _ => proof.ZipOffset+30+"package/payload.txt".Length+20 // force-ZIP64 local extra
        };
        using (var input = new FileStream(file,FileMode.Open,FileAccess.ReadWrite))
        { input.Position=offset; var value=input.ReadByte(); input.Position=offset;input.WriteByte((byte)(value^1)); }
        var output = Path.Combine(_root,"rejected");
        var error = await Assert.ThrowsExactlyAsync<ArchivePasswordEncodingException>(() => Extract(service,file,output,opening));
        Assert.AreEqual("ArchiveIntegrityFailure",error.Code);
        Assert.IsFalse(Directory.Exists(output));
        Assert.IsTrue(File.Exists(file));
    }

    [TestMethod]
    public async Task UnsupportedTailAndMultiplePlausibleArchivesNeverProduceAProof()
    {
        var file = await Fixture(1024L*1024*1024,true,32769);
        Assert.IsNull(await BoundedZipStructureProbe.FindStructureAsync(file,new FileInfo(file).Length,default));
        var ordinary = ZipStructureFixture.Zip();
        var ambiguous = await ZipStructureFixture.Write(Path.Combine(_root,"ambiguous.mp4"),600*1024*1024,
            ordinary.Concat(ordinary).ToArray());
        await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() =>
            BoundedZipStructureProbe.FindStructureAsync(ambiguous,new FileInfo(ambiguous).Length,default));
    }

    [TestMethod]
    public async Task DependencyFailureDuringExplicitDiscoveryCarriesItsGenericComponentId()
    {
        var file = Path.Combine(_root,"input.png");
        await File.WriteAllBytesAsync(file,Convert.FromHexString("89504e470d0a1a0a0000000000000000"));
        var result = await new FileProcessingPlanExecutor(new MissingReader()).ExecuteAsync(new FileProcessingPlan
            {Requirement="required",Steps=[new() {Id="open",Op="extractArchive",Selector="input.png"}]},
            _root,[file],Path.Combine(_root,"state"));
        Assert.IsFalse(result.Completed); Assert.IsFalse(result.NeedsPassword);
        Assert.AreEqual("DEPENDENCY_REQUIRED",result.FailureCode);
        Assert.AreEqual("fixture-reader-component",result.RequiredComponentId);
        Assert.IsTrue(File.Exists(file));
    }

    private sealed class MissingReader : IArchiveExtractionService
    {
        public Task<ArchiveFormatProbeResult?> ProbeAuthorizedFormatAsync(string entry,IReadOnlyList<string?> passwords,CancellationToken ct) =>
            throw new DependencyNotInstalledException("fixture","Fixture reader","Install the reader.","fixture-reader-component");
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry,IReadOnlyList<string?> passwords,Action<string>? output,CancellationToken ct) =>
            throw new AssertFailedException("Password testing must not start.");
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,Action<int>? progress,CancellationToken ct) =>
            throw new AssertFailedException("Extraction must not start.");
    }

    private sealed class Service() : CompressedFileService(null!,NullLogger<CompressedFileService>.Instance)
    {
        public int CompatibilityStarts {get;private set;}
        protected override Task<PythonRuntimeExecution> GetZipCompatibilityExecutionAsync(CancellationToken ct)
        { CompatibilityStarts++; return Task.FromResult(Runtime()); }
        protected override string? GetSevenZipExecutable() => throw new AssertFailedException("The verified media prefix must not trigger a CLI scan.");
    }
    private static PythonRuntimeExecution Runtime()
    {
        var executable=Environment.GetEnvironmentVariable("BAKABASE_TEST_PYZIPPER_PYTHON");
        if(string.IsNullOrWhiteSpace(executable)||!File.Exists(executable))Assert.Inconclusive("Set BAKABASE_TEST_PYZIPPER_PYTHON.");
        return new(new(){Mode=PythonRuntimeMode.External,PythonExecutable=executable},new("fixture","fixture",executable!,"fixture"));
    }
    private static Task<CompressedFileService.TestResult> Extract(CompressedFileService service,string file,string output,ArchiveOpenOptions options) =>
        service.ExtractWithProgress(file,output,null,false,CompressedFileService.OverwriteMode.None,
            CompressedFileService.ProgressOutputTarget.StandardOutput,null,null,null,default,options);
    private async Task<string> Fixture(long prefix,bool zip64,int trailing,bool encrypted=false)
    {
        var file=Path.Combine(_root,Guid.NewGuid().ToString("N")+".mp4");
        const string script="""
import json,sys,io,zipfile,struct,pyzipper
r=json.load(sys.stdin);b=io.BytesIO()
z=pyzipper.AESZipFile(b,'w',compression=8,encryption=pyzipper.WZ_AES) if r['encrypted'] else zipfile.ZipFile(b,'w',compression=8)
with z:
 if r['encrypted']:
  z.setpassword('known-密码'.encode('gbk'));z.setencryption(pyzipper.WZ_AES,nbits=256,force_wz_aes_version=2)
 for name,data in [('package/payload.txt',b'inert payload'),('key.txt',b'preserve key')]:
  with z.open(name,'w',force_zip64=r['zip64']) as entry:entry.write(data)
data=b.getvalue()
if r['zip64']:
 data=bytearray(data);end=len(data)-22;size,offset=struct.unpack_from('<II',data,end+12)
 # Python 3.9 rewrites small local sizes while retaining its force_zip64 extra.
 # Use canonical sentinel fields, matching real large members and the strict probe.
 p=offset
 while p<end:
  local=struct.unpack_from('<I',data,p+42)[0]
  struct.pack_into('<II',data,local+18,0xffffffff,0xffffffff)
  struct.pack_into('<H',data,local+4,45);struct.pack_into('<H',data,p+6,45)
  ns,es,cs=struct.unpack_from('<HHH',data,p+28);p+=46+ns+es+cs
 record=struct.pack('<IQHHIIQQQQ',0x06064b50,44,45,45,0,0,2,2,size,offset)
 locator=struct.pack('<IIQI',0x07064b50,0,end,1)
 data=data[:end]+record+locator+data[end:]
with open(r['file'],'wb') as f:
 f.write(b'\0\0\0\x18ftypisom');f.seek(r['prefix']);f.write(data);f.write(b'T'*r['trailing'])
""";
        using var process=new Process{StartInfo=Runtime().StartInfo(["-I","-c",script],_root)};
        process.Start();await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new{file,prefix,zip64,trailing,encrypted}));process.StandardInput.Close();
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();await output;Assert.AreEqual(0,process.ExitCode,await error);
        return file;
    }
}
