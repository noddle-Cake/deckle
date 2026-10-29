using System.Diagnostics;
using System.Runtime.InteropServices;
#if WINDOWS
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
#endif

namespace Scanwise.App.Services;

/// <summary>
/// Image clipboard and sharing, which Avalonia 11 doesn't cover (its clipboard is text only).
/// Windows uses the system clipboard and share sheet; Linux and macOS use the usual command-line tools.
/// </summary>
internal static class PlatformShell
{
    /// <summary>Puts a PNG on the clipboard as an image. Throws with a user-facing message when that isn't possible.</summary>
    public static async Task CopyImageAsync(string pngPath)
    {
#if WINDOWS
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        var file = await StorageFile.GetFileFromPathAsync(pngPath);
        package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
        Clipboard.SetContent(package);
        // Render now, so the image stays on the clipboard after Scanwise closes.
        Clipboard.Flush();
#else
        if (OperatingSystem.IsMacOS())
        {
            Platform.MacIntegration.CopyImage(pngPath);
            return;
        }
        var bytes = await File.ReadAllBytesAsync(pngPath);
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) && FindTool("wl-copy") is { } wlCopy)
        {
            await RunAsync(wlCopy, bytes, "--type", "image/png");
        }
        else if (FindTool("xclip") is { } xclip)
        {
            await RunAsync(xclip, bytes, "-selection", "clipboard", "-t", "image/png", "-i");
        }
        else
        {
            throw new InvalidOperationException("Copying images needs wl-clipboard (Wayland) or xclip (X11). Install one with your package manager.");
        }
#endif
    }

    /// <summary>
    /// Offers files to other apps: the share sheet on Windows, the share menu on macOS (pointing at
    /// <paramref name="anchor"/>, in window points), a new email on Linux.
    /// </summary>
    public static async Task ShareAsync(IReadOnlyList<string> paths, string title, IntPtr window, Avalonia.Rect anchor)
    {
#if WINDOWS
        var files = new List<IStorageItem>();
        foreach (var path in paths)
        {
            files.Add(await StorageFile.GetFileFromPathAsync(path));
        }
        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        var manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(interop.GetForWindow(window, DataTransferManagerIid));
        void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            sender.DataRequested -= OnDataRequested;
            var data = args.Request.Data;
            data.Properties.Title = title;
            data.SetStorageItems(files, readOnly: true);
            if (files.Count == 1 && files[0] is StorageFile { FileType: ".png" or ".jpg" or ".jpeg" } image)
            {
                data.SetBitmap(RandomAccessStreamReference.CreateFromFile(image));
            }
        }
        manager.DataRequested += OnDataRequested;
        interop.ShowShareUIForWindow(window);
#else
        if (OperatingSystem.IsMacOS() && window != IntPtr.Zero)
        {
            Platform.MacIntegration.Share(window, paths, anchor.X, anchor.Y, anchor.Width, anchor.Height);
            return;
        }
        if (FindTool("xdg-email") is not { } xdgEmail)
        {
            throw new InvalidOperationException("Sharing needs xdg-email (from xdg-utils). Install it with your package manager.");
        }
        var args = new List<string> { "--subject", title };
        foreach (var path in paths)
        {
            args.Add("--attach");
            args.Add(path);
        }
        await RunAsync(xdgEmail, null, [.. args]);
#endif
    }

#if WINDOWS
    private static readonly Guid DataTransferManagerIid = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }
#else
    private static string? FindTool(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(dir => Path.Combine(dir, name))
        .FirstOrDefault(File.Exists);

    /// <summary>
    /// Runs a helper, feeding it <paramref name="input"/>. wl-copy and xclip keep serving the clipboard from a
    /// background process, so only the foreground part is waited for.
    /// </summary>
    private static async Task RunAsync(string exe, byte[]? input, params string[] args)
    {
        var start = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = input is not null,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {exe}.");
        if (input is not null)
        {
            await process.StandardInput.BaseStream.WriteAsync(input);
            process.StandardInput.Close();
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return; // Still running in the foreground; it has the data, leave it be.
        }
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(exe)} failed (exit code {process.ExitCode}).");
        }
    }
#endif
}
