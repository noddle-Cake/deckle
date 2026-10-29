using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Scanwise.App.Services.Platform;

/// <summary>
/// Windows shell integration: the "Scan" task in the taskbar jump list, and a Start menu shortcut whose hotkey
/// Explorer handles (so it works even when Scanwise is closed).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsShell
{
    private const string ShortcutName = "Scanwise Scan.lnk";

    public static string StartMenuShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);

    /// <summary>Adds "Scan" to the right-click menu of Scanwise's taskbar button.</summary>
    public static void UpdateJumpList(string exePath)
    {
        var list = (ICustomDestinationList)new DestinationList();
        try
        {
            var arrayIid = typeof(IObjectArray).GUID;
            list.BeginList(out _, ref arrayIid, out var removed);
            Marshal.ReleaseComObject(removed);
            var tasks = (IObjectCollection)new EnumerableObjectCollection();
            tasks.AddObject(CreateLink(exePath, "--scan", "Scan", "Scan with the current settings", 0));
            list.AddUserTasks((IObjectArray)tasks);
            list.CommitList();
        }
        catch
        {
            list.AbortList();
            throw;
        }
        finally
        {
            Marshal.ReleaseComObject(list);
        }
    }

    /// <summary>Creates (or refreshes) the Start menu shortcut with a Ctrl+Alt+<paramref name="key"/> hotkey.</summary>
    public static void CreateHotkeyShortcut(string exePath, char key)
    {
        const short HotkeyControl = 0x02, HotkeyAlt = 0x04;
        var hotkey = (short)(((HotkeyControl | HotkeyAlt) << 8) | char.ToUpperInvariant(key));
        var link = CreateLink(exePath, "--scan", "Scanwise Scan", "Scan with Scanwise", hotkey);
        Directory.CreateDirectory(Path.GetDirectoryName(StartMenuShortcutPath)!);
        ((IPersistFile)link).Save(StartMenuShortcutPath, true);
        Marshal.ReleaseComObject(link);
        // Explorer picks up new shortcut hotkeys when it hears about the file.
        SHChangeNotify(ShcneCreate, ShcnfPathW, StartMenuShortcutPath, IntPtr.Zero);
    }

    public static void RemoveHotkeyShortcut()
    {
        if (File.Exists(StartMenuShortcutPath))
        {
            File.Delete(StartMenuShortcutPath);
            SHChangeNotify(ShcneDelete, ShcnfPathW, StartMenuShortcutPath, IntPtr.Zero);
        }
    }

    private static IShellLinkW CreateLink(string exePath, string arguments, string title, string description, short hotkey)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(exePath);
        link.SetArguments(arguments);
        link.SetWorkingDirectory(Path.GetDirectoryName(exePath)!);
        link.SetDescription(description);
        link.SetIconLocation(exePath, 0);
        if (hotkey != 0)
        {
            link.SetHotkey(hotkey);
        }
        // Jump list tasks show the Title property, not the file name.
        var store = (IPropertyStore)link;
        var key = new PropertyKey(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
        var value = new PropVariant { VarType = 31 /* VT_LPWSTR */, Pointer = Marshal.StringToCoTaskMemUni(title) };
        try
        {
            store.SetValue(ref key, ref value);
            store.Commit();
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Pointer);
        }
        return link;
    }

    private const int ShcneCreate = 0x2, ShcneDelete = 0x4;
    private const uint ShcnfPathW = 0x5;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

    [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")]
    private class DestinationList;

    [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a")]
    private class EnumerableObjectCollection;

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void BeginList(out uint minSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectArray items);
        void AppendKnownCategory(int category);
        void AddUserTasks(IObjectArray tasks);
        void CommitList();
        void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string? appId);
        void AbortList();
    }

    [ComImport, Guid("92ca9dcd-5622-4bba-a805-5e9f541bd8c9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);
    }

    [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        // IObjectArray
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);
        // IObjectCollection
        void AddObject([MarshalAs(UnmanagedType.Interface)] object item);
        void AddFromArray(IObjectArray source);
        void RemoveObjectAt(uint index);
        void Clear();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relPath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly uint PropertyId = propertyId;
    }

    /// <summary>PROPVARIANT, only as far as a string value needs (24 bytes on 64-bit, padded).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VarType;
        [FieldOffset(8)] public IntPtr Pointer;
    }
}
