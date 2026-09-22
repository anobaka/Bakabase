using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Bakabase.Shell.Controls;

// Load the production declarations, rather than copying their CGRect signatures
// into a test where the same mistake could go unnoticed in the real shell.
var reportPath = args.Length == 2 && args[0] == "--report" ? Path.GetFullPath(args[1]) : null;
if (args.Length != 0 && reportPath == null)
    throw new ArgumentException("Usage: Bakabase.Shell.NativeAbiProbe [--report NEW_PATH]");
if (reportPath != null && File.Exists(reportPath))
    throw new IOException("Probe report must not overwrite existing evidence");
var report = new Dictionary<string, object?>
{
    ["passed"] = false,
    ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
    ["framework"] = RuntimeInformation.FrameworkDescription,
    ["shellAssembly"] = typeof(NativeWebViewHost).Assembly.Location,
    ["scope"] = "Real shell P/Invoke CGRect arguments; Foundation NSValue round trip; no GUI or product startup"
};
try
{
    if (!OperatingSystem.IsMacOS() || RuntimeInformation.ProcessArchitecture is not (Architecture.Arm64 or Architecture.X64))
        throw new PlatformNotSupportedException("Native CGRect probe requires macOS arm64 or x64");
    var bridge = typeof(NativeWebViewHost).GetNestedType("ObjC", BindingFlags.NonPublic)
                 ?? throw new InvalidOperationException("Production Objective-C bridge is missing");
    var rectangle = bridge.GetNestedType("CGRect", BindingFlags.Public)
                    ?? throw new InvalidOperationException("Production CGRect is missing");
    var create = Method("SendIntPtr_CGRect_IntPtr");
    var set = Method("SendVoid_CGRect");
    Require(create.GetParameters().Select(p => p.ParameterType).SequenceEqual(
        new[] {typeof(IntPtr), typeof(IntPtr), rectangle, typeof(IntPtr)}), "Creation signature is not a by-value CGRect plus pointer");
    Require(set.GetParameters().Select(p => p.ParameterType).SequenceEqual(
        new[] {typeof(IntPtr), typeof(IntPtr), rectangle}), "Frame signature is not a by-value CGRect");
    Require(Marshal.SizeOf(rectangle) == 32, "CGRect must contain four 64-bit CGFloat fields");
    foreach (var (field, index) in new[] {"X", "Y", "Width", "Height"}.Select((field, index) => (field, index)))
        Require(Marshal.OffsetOf(rectangle, field).ToInt64() == index * 8, "Wrong CGRect field offset: " + field);

    NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");
    NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "libBakabaseNativeAbiProbe.dylib"));
    var pool = Send(Send(Class("NSAutoreleasePool"), "alloc"), "init");
    var probe = IntPtr.Zero;
    var cases = new List<object>();
    try
    {
        probe = Send(Send(Class("BakabaseNativeRectProbe"), "alloc"), "init");
        Require(probe != IntPtr.Zero, "Native Foundation probe could not be initialized");
        foreach (var expected in new[] {new double[] {0, 0, 0, 0}, new[] {1.25, -2.5, 317.75, 249.5}, new[] {-99.5, 71.25, 0.5, 1024.125}})
        {
            var value = Activator.CreateInstance(rectangle, expected.Cast<object>().ToArray())!;
            var captured = (IntPtr)create.Invoke(null, new[] {(object)probe, Selector("captureRect:cookie:"), value, new IntPtr(0x5A17)})!;
            CheckValue(captured, expected);
            set.Invoke(null, new[] {(object)probe, Selector("setCapturedRect:"), value});
            CheckValue(Send(probe, "capturedRect"), expected);
            cases.Add(new {rect = expected, creationAndTrailingPointer = true, voidFrameSetter = true});
        }
    }
    finally
    {
        if (probe != IntPtr.Zero) Method("SendVoid").Invoke(null, new object[] {probe, Selector("release")});
        Method("SendVoid").Invoke(null, new object[] {pool, Selector("drain")});
    }
    report["cases"] = cases;
    report["passed"] = true;

    MethodInfo Method(string name) => bridge.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("Production bridge method is missing: " + name);
    IntPtr Class(string name) => (IntPtr)Method("objc_getClass").Invoke(null, new object[] {name})!;
    IntPtr Selector(string name) => (IntPtr)Method("Sel").Invoke(null, new object[] {name})!;
    IntPtr Send(IntPtr target, string name) => (IntPtr)Method("SendIntPtr").Invoke(null, new object[] {target, Selector(name)})!;
    void CheckValue(IntPtr value, double[] expected)
    {
        Require(value != IntPtr.Zero, "CGRect creation lost its trailing pointer or native return value");
        var buffer = Marshal.AllocHGlobal(32);
        try
        {
            Native.GetValue(value, Selector("getValue:size:"), buffer, 32);
            var actual = new double[4];
            Marshal.Copy(buffer, actual, 0, 4);
            Require(actual.SequenceEqual(expected), "Native CGRect differs: " + JsonSerializer.Serialize(actual));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
catch (Exception error)
{
    report["error"] = error.ToString();
}
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions {WriteIndented = true});
if (reportPath != null) File.WriteAllText(reportPath, json + Environment.NewLine);
Console.WriteLine(json);
return report["passed"] is true ? 0 : 1;

static void Require(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

internal static class Native
{
    // NSValue's pointer-based getter avoids any struct-return ABI/objc_msgSend_stret.
    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    internal static extern void GetValue(IntPtr receiver, IntPtr selector, IntPtr buffer, nuint size);
}
