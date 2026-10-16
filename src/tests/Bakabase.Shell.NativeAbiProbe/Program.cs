using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Bakabase.Shell.Controls;

// Load the production declarations, rather than copying their native signatures
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
    ["scope"] = "Real shell P/Invoke CGRect and NSInteger/BOOL arguments and profile NSUUID construction; Foundation round trips; no GUI or product startup"
};
try
{
    if (!OperatingSystem.IsMacOS() || RuntimeInformation.ProcessArchitecture is not (Architecture.Arm64 or Architecture.X64))
        throw new PlatformNotSupportedException("Native ABI probe requires macOS arm64 or x64");
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
        // Call the real production NSInteger/BOOL declaration against a Foundation-only
        // receiver. This exercises objc_msgSend's ABI without creating NSApplication or
        // changing this process's Dock visibility. Native storage checks the entire value,
        // including high bits that would be lost by an accidental 32-bit declaration.
        var dockVisibility = typeof(NativeWebViewHost).Assembly.GetType("Bakabase.Shell.Components.MacOSDockVisibility")
            ?? throw new InvalidOperationException("Production Dock visibility bridge is missing");
        var setActivationPolicy = dockVisibility.GetMethod("SetActivationPolicy", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Production activation-policy declaration is missing");
        Require(setActivationPolicy.GetParameters().Select(p => p.ParameterType).SequenceEqual(
            new[] {typeof(IntPtr), typeof(IntPtr), typeof(nint)}), "Activation policy must be a pointer-sized NSInteger");
        Require(setActivationPolicy.ReturnType == typeof(bool)
                && setActivationPolicy.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>()?.Value == UnmanagedType.I1,
            "Activation policy must return a one-byte Objective-C BOOL");
        var policyCases = new List<object>();
        foreach (var (policy, expectedResult) in new[] {(0L, true), (1L, false), (0x100000001L, true), (-0x100000001L, false)})
        {
            var result = (bool)setActivationPolicy.Invoke(null,
                new object[] {probe, Selector("capturePolicy:"), new IntPtr(policy)})!;
            Require(result == expectedResult, "Native BOOL return differs for policy " + policy);
            var value = Send(probe, "capturedPolicy");
            Require(value != IntPtr.Zero, "Native policy capture returned nil");
            var buffer = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Native.GetValue(value, Selector("getValue:size:"), buffer, (nuint)IntPtr.Size);
                Require(Marshal.ReadIntPtr(buffer).ToInt64() == policy, "Native NSInteger differs for policy " + policy);
            }
            finally { Marshal.FreeHGlobal(buffer); }
            policyCases.Add(new {policy, nativeRoundTrip = true, booleanResult = result});
        }
        report["dockActivationPolicy"] = policyCases;
        // Execute the same Foundation allocation/initializer used when a named
        // profile opens its WebView. A source-string assertion cannot detect an
        // Objective-C selector that exists on no native class and aborts the app.
        var createIdentifier = typeof(NativeWebViewHost).GetMethod("CreateMacOSProfileIdentifier",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Production profile identifier factory is missing");
        var anchor = Path.Combine(Path.GetTempPath(), "bakabase-native-profile-probe", "profile-a");
        var first = ProfileIdentifier(anchor);
        var same = ProfileIdentifier(anchor + Path.DirectorySeparatorChar);
        var other = ProfileIdentifier(Path.Combine(Path.GetDirectoryName(anchor)!, "profile-b"));
        Require(first == same, "The same profile changed its native browser identifier");
        Require(first != other, "Different profiles share a native browser identifier");
        report["profileIdentifier"] = new {nativeRoundTrip = true, stableForSameAnchor = true, distinctForDifferentAnchors = true};

        Guid ProfileIdentifier(string path)
        {
            var identifier = (IntPtr)createIdentifier.Invoke(null, new object[] {path})!;
            Require(identifier != IntPtr.Zero, "Profile identifier allocation returned nil");
            try
            {
                var value = Marshal.PtrToStringUTF8(Send(Send(identifier, "UUIDString"), "UTF8String"));
                Require(Guid.TryParse(value, out var uuid), "Foundation did not return a valid UUID string");
                Require(uuid != Guid.Empty, "Profile identifier must not be empty");
                return uuid;
            }
            finally { Method("SendVoid").Invoke(null, new object[] {identifier, Selector("release")}); }
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
