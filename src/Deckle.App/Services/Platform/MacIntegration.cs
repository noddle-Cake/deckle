using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Deckle.App.Services.Platform;

/// <summary>
/// macOS integration: a "Scan" item in the Dock icon's menu, a global Control+Option+key hotkey (Carbon's
/// RegisterEventHotKey, which needs no accessibility permission) that works while Deckle is running, the image
/// pasteboard, the system share menu, and the window's edited dot and proxy icon.
/// Avalonia 11 has no dock-menu API, so the menu is added to its app delegate through the Objective-C runtime.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe class MacIntegration
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    private static Action? _onScan;
    private static Action? _onScanNew;
    private static IntPtr _dockMenu;
    private static IntPtr _hotKeyRef;
    private static bool _hotKeyHandlerInstalled;

    public static void InstallDockMenu(Action onScan, Action onScanNew)
    {
        _onScan = onScan;
        _onScanNew = onScanNew;
        var app = Send(Class("NSApplication"), Sel("sharedApplication"));
        var appDelegate = Send(app, Sel("delegate"));
        if (appDelegate == IntPtr.Zero)
        {
            return;
        }
        var delegateClass = object_getClass(appDelegate);
        class_addMethod(delegateClass, Sel("applicationDockMenu:"),
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)&DockMenu, "@@:@");
        class_addMethod(delegateClass, Sel("deckleScan:"),
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnScanMenuItem, "v@:@");
        class_addMethod(delegateClass, Sel("deckleScanNew:"),
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnScanNewMenuItem, "v@:@");

        var menu = Send(Send(Class("NSMenu"), Sel("alloc")), Sel("init"));
        var noKey = NSString("");
        foreach (var (title, action) in new[] { ("Scan", "deckleScan:"), ("Scan into New Document", "deckleScanNew:") })
        {
            var item = Send(Send(Class("NSMenuItem"), Sel("alloc")), Sel("initWithTitle:action:keyEquivalent:"), NSString(title), Sel(action), noKey);
            Send(item, Sel("setTarget:"), appDelegate);
            Send(menu, Sel("addItem:"), item);
        }
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
    private static void OnScanNewMenuItem(IntPtr self, IntPtr cmd, IntPtr sender)
    {
        try
        {
            _onScanNew?.Invoke();
        }
        catch (Exception)
        {
        }
    }

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

    /// <summary>Puts a PNG on the general pasteboard as PNG and TIFF (TIFF for older apps), without AppleScript.</summary>
    public static void CopyImage(string pngPath)
    {
        var data = Send(Class("NSData"), Sel("dataWithContentsOfFile:"), NSString(pngPath));
        if (data == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not read the page image.");
        }
        var board = Send(Class("NSPasteboard"), Sel("generalPasteboard"));
        Send(board, Sel("clearContents"));
        Send(board, Sel("setData:forType:"), data, NSString("public.png"));
        var image = Send(Send(Class("NSImage"), Sel("alloc")), Sel("initWithData:"), data);
        if (image != IntPtr.Zero)
        {
            var tiff = Send(image, Sel("TIFFRepresentation"));
            if (tiff != IntPtr.Zero)
            {
                Send(board, Sel("setData:forType:"), tiff, NSString("public.tiff"));
            }
            Send(image, Sel("release"));
        }
    }

    private static IntPtr _sharePicker;

    /// <summary>
    /// Shows the system share menu (AirDrop, Mail, Messages, Notes…) for <paramref name="paths"/>, pointing at a
    /// rectangle in the window's content view, given in points from the top left.
    /// </summary>
    public static void Share(IntPtr nsView, IReadOnlyList<string> paths, double x, double y, double width, double height)
    {
        var items = Send(Class("NSMutableArray"), Sel("array"));
        foreach (var path in paths)
        {
            Send(items, Sel("addObject:"), Send(Class("NSURL"), Sel("fileURLWithPath:"), NSString(path)));
        }
        if (_sharePicker != IntPtr.Zero)
        {
            Send(_sharePicker, Sel("release")); // The previous picker has closed by now.
        }
        _sharePicker = Send(Send(Class("NSSharingServicePicker"), Sel("alloc")), Sel("initWithItems:"), items);
        // AppKit views count y from the bottom unless they're flipped.
        if ((Send(nsView, Sel("isFlipped")) & 0xFF) == 0)
        {
            var viewHeight = ViewHeight(nsView);
            y = viewHeight - y - height;
        }
        const nuint MinYEdge = 1;
        ShowPicker(_sharePicker, Sel("showRelativeToRect:ofView:preferredEdge:"), new NSRect(x, y, width, height), nsView, MinYEdge);
    }

    /// <summary>The dot in the window's close button for unsaved changes, and the proxy icon for the saved file.</summary>
    public static void SetDocumentState(IntPtr nsView, bool edited, string? path)
    {
        var window = Send(nsView, Sel("window"));
        if (window == IntPtr.Zero)
        {
            return;
        }
        Send(window, Sel("setDocumentEdited:"), edited ? 1 : IntPtr.Zero);
        Send(window, Sel("setRepresentedFilename:"), NSString(path ?? ""));
    }

    /// <summary>Text on the Dock icon's badge (scan progress); null removes it.</summary>
    public static void SetDockBadge(string? text)
    {
        var app = Send(Class("NSApplication"), Sel("sharedApplication"));
        var tile = Send(app, Sel("dockTile"));
        Send(tile, Sel("setBadgeLabel:"), text is null ? IntPtr.Zero : NSString(text));
    }

    /// <summary>
    /// Adds a Liquid Glass panel (NSGlassEffectView, macOS 26+) behind Avalonia's view, in the same container
    /// that holds Avalonia's blur view, so the sidebar's controls draw on top of it. Returns the view, or zero
    /// where Liquid Glass doesn't exist (macOS 15 and earlier keep the blur from AcrylicBlur).
    /// </summary>
    public static IntPtr AddGlassPanel(IntPtr nsView, double cornerRadius)
    {
        var glassClass = Class("NSGlassEffectView");
        var container = Send(nsView, Sel("superview"));
        if (glassClass == IntPtr.Zero || container == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }
        var glass = Send(Send(glassClass, Sel("alloc")), Sel("init"));
        if (glass == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }
        SendDouble(glass, Sel("setCornerRadius:"), cornerRadius);
        // Keeps its top and bottom gaps when the window is resized; the width follows the sidebar.
        const nint MaxXMargin = 4, HeightSizable = 16;
        Send(glass, Sel("setAutoresizingMask:"), MaxXMargin | HeightSizable);
        Send(glass, Sel("setHidden:"), 1);
        const nint Below = -1; // NSWindowBelow
        Send(container, Sel("addSubview:positioned:relativeTo:"), glass, Below, nsView);
        return glass; // Owned by the container now, and by us until the window closes.
    }

    /// <summary>Places the glass panel, or hides it when <paramref name="frame"/> is null.</summary>
    public static void SetGlassPanelFrame(IntPtr glass, (double X, double Y, double Width, double Height)? frame)
    {
        if (frame is not { } f)
        {
            Send(glass, Sel("setHidden:"), 1);
            return;
        }
        SetFrame(glass, Sel("setFrame:"), new NSRect(f.X, f.Y, f.Width, f.Height));
        Send(glass, Sel("setHidden:"), IntPtr.Zero);
    }

    public static void RemoveGlassPanel(IntPtr glass)
    {
        Send(glass, Sel("removeFromSuperview"));
        Send(glass, Sel("release"));
    }

    // ---------------------------------------------------------------- Read-back for the self-check (9.5)

    public readonly record struct MenuEntry(string Menu, string Title, string Key, ulong Modifiers);

    public const ulong ShiftMask = 1 << 17, ControlMask = 1 << 18, OptionMask = 1 << 19, CommandMask = 1 << 20;

    /// <summary>Every item of the menu bar as AppKit has it: menu title, item title, key equivalent, modifiers.</summary>
    public static List<MenuEntry> MenuBarItems()
    {
        var result = new List<MenuEntry>();
        var mainMenu = Send(Send(Class("NSApplication"), Sel("sharedApplication")), Sel("mainMenu"));
        foreach (var top in Items(Send(mainMenu, Sel("itemArray"))))
        {
            var menuTitle = Str(Send(top, Sel("title")));
            var submenu = Send(top, Sel("submenu"));
            if (submenu == IntPtr.Zero)
            {
                continue;
            }
            CollectItems(submenu, menuTitle, result);
        }
        return result;
    }

    private static void CollectItems(IntPtr menu, string menuTitle, List<MenuEntry> result)
    {
        foreach (var item in Items(Send(menu, Sel("itemArray"))))
        {
            if (IsTrue(Send(item, Sel("isSeparatorItem"))))
            {
                continue;
            }
            var title = Str(Send(item, Sel("title")));
            result.Add(new MenuEntry(menuTitle, title, Str(Send(item, Sel("keyEquivalent"))),
                (ulong)Send(item, Sel("keyEquivalentModifierMask"))));
            var submenu = Send(item, Sel("submenu"));
            if (submenu != IntPtr.Zero)
            {
                CollectItems(submenu, $"{menuTitle} ▸ {title}", result);
            }
        }
    }

    /// <summary>The Dock menu's item titles, asked from the app delegate as the Dock would.</summary>
    public static List<string> DockMenuTitles()
    {
        var app = Send(Class("NSApplication"), Sel("sharedApplication"));
        var appDelegate = Send(app, Sel("delegate"));
        if (appDelegate == IntPtr.Zero || !IsTrue(Send(appDelegate, Sel("respondsToSelector:"), Sel("applicationDockMenu:"))))
        {
            return [];
        }
        var menu = Send(appDelegate, Sel("applicationDockMenu:"), app);
        return menu == IntPtr.Zero ? [] : Items(Send(menu, Sel("itemArray"))).Select(i => Str(Send(i, Sel("title")))).ToList();
    }

    public static List<string> PasteboardTypes() =>
        Items(Send(Send(Class("NSPasteboard"), Sel("generalPasteboard")), Sel("types"))).Select(Str).ToList();

    public static bool IsDocumentEdited(IntPtr nsView) => IsTrue(Send(Send(nsView, Sel("window")), Sel("isDocumentEdited")));

    public static string RepresentedFilename(IntPtr nsView) => Str(Send(Send(nsView, Sel("window")), Sel("representedFilename")));

    public static string DockBadge()
    {
        var tile = Send(Send(Class("NSApplication"), Sel("sharedApplication")), Sel("dockTile"));
        return Str(Send(tile, Sel("badgeLabel")));
    }

    public static long WindowNumber(IntPtr nsView) => (long)Send(Send(nsView, Sel("window")), Sel("windowNumber"));

    public static string BundleIdentifier() => Str(Send(Send(Class("NSBundle"), Sel("mainBundle")), Sel("bundleIdentifier")));

    public static bool HasClass(string name) => Class(name) != IntPtr.Zero;

    public static string ClassName(IntPtr obj) => obj == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(object_getClassName(obj)) ?? "";

    public readonly record struct GlassState(bool SameContainer, int GlassIndex, int ViewIndex, bool Hidden, double Width, double Height, string Container);

    /// <summary>Where the glass panel sits relative to Avalonia's view: it must share the container and come first.</summary>
    public static GlassState Glass(IntPtr glass, IntPtr nsView)
    {
        var container = Send(nsView, Sel("superview"));
        var subviews = Items(Send(container, Sel("subviews")));
        var frame = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? FrameArm64(glass, Sel("frame")) : FrameX64(glass, Sel("frame"));
        return new GlassState(Send(glass, Sel("superview")) == container, Array.IndexOf(subviews, glass), Array.IndexOf(subviews, nsView),
            IsTrue(Send(glass, Sel("isHidden"))), frame.Width, frame.Height, ClassName(container));
    }

    private static IntPtr[] Items(IntPtr nsArray)
    {
        if (nsArray == IntPtr.Zero)
        {
            return [];
        }
        var count = (long)Send(nsArray, Sel("count"));
        var items = new IntPtr[count];
        for (var i = 0; i < count; i++)
        {
            items[i] = Send(nsArray, Sel("objectAtIndex:"), (IntPtr)i);
        }
        return items;
    }

    private static string Str(IntPtr nsString) =>
        nsString == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(Send(nsString, Sel("UTF8String"))) ?? "";

    /// <summary>BOOL comes back in the low byte of the return register.</summary>
    private static bool IsTrue(IntPtr value) => (value & 0xFF) != 0;

    private static double ViewHeight(IntPtr nsView)
    {
        // -frame returns an NSRect; read it with the struct-returning entry point on x64.
        var frame = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? FrameArm64(nsView, Sel("frame"))
            : FrameX64(nsView, Sel("frame"));
        return frame.Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NSRect(double X, double Y, double Width, double Height);

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
    [DllImport(ObjC)] private static extern IntPtr object_getClassName(IntPtr obj);
    [DllImport(ObjC)] private static extern bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendDouble(IntPtr receiver, IntPtr selector, double arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SetFrame(IntPtr receiver, IntPtr selector, NSRect frame);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void ShowPicker(IntPtr receiver, IntPtr selector, NSRect rect, IntPtr view, nuint edge);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern NSRect FrameArm64(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend_stret")] private static extern void FrameX64Stret(out NSRect result, IntPtr receiver, IntPtr selector);

    private static NSRect FrameX64(IntPtr receiver, IntPtr selector)
    {
        FrameX64Stret(out var rect, receiver, selector);
        return rect;
    }

    [DllImport(Carbon)] private static extern IntPtr GetApplicationEventTarget();
    [DllImport(Carbon)] private static extern int InstallEventHandler(IntPtr target, IntPtr handler, uint numTypes, ref EventTypeSpec list, IntPtr userData, out IntPtr handlerRef);
    [DllImport(Carbon)] private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id, IntPtr target, uint options, out IntPtr hotKeyRef);
    [DllImport(Carbon)] private static extern int UnregisterEventHotKey(IntPtr hotKeyRef);
}
