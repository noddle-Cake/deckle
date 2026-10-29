using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Scanwise.App.Services.Platform;

/// <summary>
/// macOS integration: a "Scan" item in the Dock icon's menu, and a global Control+Option+key hotkey (Carbon's
/// RegisterEventHotKey, which needs no accessibility permission) that works while Scanwise is running.
/// Avalonia 11 has no dock-menu API, so the menu is added to its app delegate through the Objective-C runtime.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe class MacIntegration
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    private static Action? _onScan;
    private static IntPtr _dockMenu;
    private static IntPtr _hotKeyRef;
    private static bool _hotKeyHandlerInstalled;

    public static void InstallDockMenu(Action onScan)
    {
        _onScan = onScan;
        var app = Send(Class("NSApplication"), Sel("sharedApplication"));
        var appDelegate = Send(app, Sel("delegate"));
        if (appDelegate == IntPtr.Zero)
        {
            return;
        }
        var delegateClass = object_getClass(appDelegate);
        class_addMethod(delegateClass, Sel("applicationDockMenu:"),
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)&DockMenu, "@@:@");
        class_addMethod(delegateClass, Sel("scanwiseScan:"),
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnScanMenuItem, "v@:@");

        var menu = Send(Send(Class("NSMenu"), Sel("alloc")), Sel("init"));
        var title = NSString("Scan");
        var noKey = NSString("");
        var item = Send(Send(Class("NSMenuItem"), Sel("alloc")), Sel("initWithTitle:action:keyEquivalent:"), title, Sel("scanwiseScan:"), noKey);
        Send(item, Sel("setTarget:"), appDelegate);
        Send(menu, Sel("addItem:"), item);
        _dockMenu = menu; // Kept for the life of the app.
        // NSApplication looks up which optional methods its delegate implements when the delegate is set.
        Send(app, Sel("setDelegate:"), appDelegate);
    }

    /// <summary>Registers Control+Option+<paramref name="key"/>; replaces any earlier registration.</summary>
    public static void RegisterHotkey(char key, Action onScan)
    {
        _onScan = onScan;
        UnregisterHotkey();
        if (!KeyCodes.TryGetValue(char.ToUpperInvariant(key), out var keyCode))
        {
            throw new ArgumentException($"Unsupported key {key}.", nameof(key));
        }
        var target = GetApplicationEventTarget();
        if (!_hotKeyHandlerInstalled)
        {
            var spec = new EventTypeSpec { EventClass = 0x6B657962 /* 'keyb' */, EventKind = 5 /* kEventHotKeyPressed */ };
            var status = InstallEventHandler(target, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, int>)&OnHotKey, 1, ref spec, IntPtr.Zero, out _);
            if (status != 0)
            {
                throw new InvalidOperationException($"Could not listen for the shortcut (error {status}).");
            }
            _hotKeyHandlerInstalled = true;
        }
        const uint ControlKey = 0x1000, OptionKey = 0x0800;
        var id = new EventHotKeyId { Signature = 0x53636E77 /* 'Scnw' */, Id = 1 };
        var result = RegisterEventHotKey(keyCode, ControlKey | OptionKey, id, target, 0, out _hotKeyRef);
        if (result != 0)
        {
            _hotKeyRef = IntPtr.Zero;
            throw new InvalidOperationException("That shortcut is already used by another app. Pick a different key.");
        }
    }

    public static void UnregisterHotkey()
    {
        if (_hotKeyRef != IntPtr.Zero)
        {
            UnregisterEventHotKey(_hotKeyRef);
            _hotKeyRef = IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr DockMenu(IntPtr self, IntPtr cmd, IntPtr sender) => _dockMenu;

    [UnmanagedCallersOnly]
    private static void OnScanMenuItem(IntPtr self, IntPtr cmd, IntPtr sender) => RaiseScan();

    [UnmanagedCallersOnly]
    private static int OnHotKey(IntPtr callRef, IntPtr eventRef, IntPtr userData)
    {
        RaiseScan();
        return 0;
    }

    private static void RaiseScan()
    {
        // An exception escaping into native code would take the app down.
        try
        {
            _onScan?.Invoke();
        }
        catch (Exception)
        {
        }
    }

    // Carbon virtual key codes (kVK_ANSI_*) follow the physical US layout, not the alphabet.
    private static readonly Dictionary<char, uint> KeyCodes = new()
    {
        ['A'] = 0x00, ['S'] = 0x01, ['D'] = 0x02, ['F'] = 0x03, ['H'] = 0x04, ['G'] = 0x05, ['Z'] = 0x06,
        ['X'] = 0x07, ['C'] = 0x08, ['V'] = 0x09, ['B'] = 0x0B, ['Q'] = 0x0C, ['W'] = 0x0D, ['E'] = 0x0E,
        ['R'] = 0x0F, ['Y'] = 0x10, ['T'] = 0x11, ['O'] = 0x1F, ['U'] = 0x20, ['I'] = 0x22, ['P'] = 0x23,
        ['L'] = 0x25, ['J'] = 0x26, ['K'] = 0x28, ['N'] = 0x2D, ['M'] = 0x2E,
    };

    private static IntPtr Class(string name) => objc_getClass(name);
    private static IntPtr Sel(string name) => sel_registerName(name);

    private static IntPtr NSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send(Class("NSString"), Sel("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] private static extern IntPtr object_getClass(IntPtr obj);
    [DllImport(ObjC)] private static extern bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

    [DllImport(Carbon)] private static extern IntPtr GetApplicationEventTarget();
    [DllImport(Carbon)] private static extern int InstallEventHandler(IntPtr target, IntPtr handler, uint numTypes, ref EventTypeSpec list, IntPtr userData, out IntPtr handlerRef);
    [DllImport(Carbon)] private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id, IntPtr target, uint options, out IntPtr hotKeyRef);
    [DllImport(Carbon)] private static extern int UnregisterEventHotKey(IntPtr hotKeyRef);
}
