using System;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace Bakabase.Shell.Components;

/// <summary>Process-local presentation for the coordinator's temporary Setup window.</summary>
public static class MacOSDockVisibility
{
    public static void SetVisible(bool visible)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Dispatcher.UIThread.VerifyAccess();

        // Avalonia's ShowInDock option is startup-only. Accessory still permits windows;
        // Regular restores the Dock, Cmd-Tab and app menu while Setup needs user input.
        var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        nint policy = visible ? 0 : 1;
        if (app != IntPtr.Zero && Send(app, sel_registerName("activationPolicy")) == policy) return;
        if (app == IntPtr.Zero || !SetActivationPolicy(app, sel_registerName("setActivationPolicy:"), policy))
            Console.Error.WriteLine($"Could not set the Setup coordinator's Dock visibility to {visible}.");
    }

    [DllImport("libobjc.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    // Objective-C BOOL is one byte; NSApplicationActivationPolicy is NSInteger.
    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SetActivationPolicy(IntPtr receiver, IntPtr selector, nint policy);
}
