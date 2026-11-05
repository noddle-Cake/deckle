using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Deckle.App.Services;
using Deckle.App.Services.Platform;
using Deckle.App.ViewModels;
using Deckle.Core.Settings;

namespace Deckle.App.Tests;

public sealed class ShortcutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "deckle-shortcut-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SecondLaunchHandsItsRequestToTheRunningInstance()
    {
        var name = "deckle-test-" + Guid.NewGuid().ToString("N")[..8];
        Assert.False(SingleInstance.TrySend(SingleInstance.ScanRequest, name));

        var received = new List<string>();
        var got = new SemaphoreSlim(0);
        using (var instance = new SingleInstance(name))
        {
            instance.Listen(r =>
            {
                lock (received)
                {
                    received.Add(r);
                }
                got.Release();
            });
            Assert.True(await RetrySendAsync(SingleInstance.ScanRequest, name));
            Assert.True(await got.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(SingleInstance.TrySend("rm -rf /", name)); // Delivered but ignored.
            Assert.True(SingleInstance.TrySend(SingleInstance.ShowRequest, name));
            Assert.True(await got.WaitAsync(TimeSpan.FromSeconds(5)));
            lock (received)
            {
                Assert.Equal([SingleInstance.ScanRequest, SingleInstance.ShowRequest], received);
            }
        }

        // After the first instance quits, the next one can take over the name.
        await Task.Delay(200);
        using var next = new SingleInstance(name);
        next.Listen(_ => got.Release());
        Assert.True(await RetrySendAsync(SingleInstance.ShowRequest, name));
        Assert.True(await got.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task CrashedInstanceLeftoverSocketDoesNotBlockTheNextOne()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows pipes vanish with their process.
        }
        var name = "deckle-test-" + Guid.NewGuid().ToString("N")[..8];
        // What a killed instance leaves behind: a socket file nobody listens on. (Kept open because .NET deletes
        // the file when a bound socket is disposed; bound but not listening refuses connections just the same.)
        var stale = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + name);
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
        socket.Bind(new System.Net.Sockets.UnixDomainSocketEndPoint(stale));
        Assert.True(Path.Exists(stale));
        Assert.False(SingleInstance.TrySend(SingleInstance.ScanRequest, name));

        var got = new SemaphoreSlim(0);
        using var instance = new SingleInstance(name);
        instance.Listen(_ => got.Release());
        Assert.True(await RetrySendAsync(SingleInstance.ScanRequest, name));
        Assert.True(await got.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>The listener starts on a background task, so give it a moment to create the pipe.</summary>
    private static async Task<bool> RetrySendAsync(string request, string name)
    {
        for (var i = 0; i < 20; i++)
        {
            if (SingleInstance.TrySend(request, name))
            {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }

    [Fact]
    public void LinuxLauncherHasAScanActionAndLeavesOtherInstallsAlone()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        var dataHome = Path.Combine(_root, "share");
        var previous = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", dataHome);
        try
        {
            var exe = Path.Combine(_root, "Scan wise", "Deckle");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "");
            LinuxDesktop.InstallLauncher(exe, () => new MemoryStream([1, 2, 3]));
            var path = LinuxDesktop.DesktopFilePath;
            Assert.StartsWith(dataHome, path);
            var text = File.ReadAllText(path);
            Assert.Contains("Actions=Scan;", text);
            Assert.Contains($"Exec=\"{exe}\" --scan", text);
            Assert.True(File.Exists(Path.Combine(dataHome, "icons", "hicolor", "256x256", "apps", "deckle.png")));
            if (File.Exists("/usr/bin/desktop-file-validate"))
            {
                var validate = Process.Start(new ProcessStartInfo("/usr/bin/desktop-file-validate", [path]) { RedirectStandardOutput = true })!;
                var output = validate.StandardOutput.ReadToEnd();
                validate.WaitForExit();
                Assert.True(validate.ExitCode == 0, output);
            }

            // Another copy that still exists keeps the launcher...
            var other = Path.Combine(_root, "other", "Deckle");
            Directory.CreateDirectory(Path.GetDirectoryName(other)!);
            LinuxDesktop.InstallLauncher(other, () => new MemoryStream([1]));
            Assert.Contains($"Exec=\"{exe}\" --scan", File.ReadAllText(path));
            // ...but once it's gone, this copy takes over.
            File.Delete(exe);
            LinuxDesktop.InstallLauncher(other, () => new MemoryStream([1]));
            Assert.Contains($"Exec={other} --scan", File.ReadAllText(path));

            // A launcher Deckle didn't write (a package, the user) is never touched.
            File.WriteAllText(path, "[Desktop Entry]\nName=Mine\n");
            LinuxDesktop.InstallLauncher(other, () => new MemoryStream([1]));
            Assert.Equal("[Desktop Entry]\nName=Mine\n", File.ReadAllText(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previous);
        }
    }

    /// <summary>
    /// Uses GLib's keyfile settings backend in a temporary folder, so the real GNOME settings (dconf) are never
    /// touched. Opt in with DECKLE_TEST_GSETTINGS=1.
    /// </summary>
    [Fact]
    public async Task GnomeShortcutIsAddedNextToExistingOnesAndRemoved()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("DECKLE_TEST_GSETTINGS") != "1")
        {
            return;
        }
        var config = Path.Combine(_root, "config-home");
        Directory.CreateDirectory(config);
        var saved = (Backend: Environment.GetEnvironmentVariable("GSETTINGS_BACKEND"), Config: Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));
        Environment.SetEnvironmentVariable("GSETTINGS_BACKEND", "keyfile");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", config);
        try
        {
            await GnomeShortcutScenario(Path.Combine(config, "glib-2.0", "settings", "keyfile"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GSETTINGS_BACKEND", saved.Backend);
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", saved.Config);
        }
    }

    private static async Task GnomeShortcutScenario(string keyfile)
    {
        const string schema = "org.gnome.settings-daemon.plugins.media-keys";
        await Gsettings("set", schema, "custom-keybindings", "['/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/']");
        Assert.True(File.Exists(keyfile), "gsettings did not use the temporary keyfile backend");

        await LinuxDesktop.SetShortcutAsync("/opt/scan wise/Deckle", 'S');
        Assert.Equal("['/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/', '/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/deckle/']",
            await Gsettings("get", schema, "custom-keybindings"));
        const string binding = "org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/deckle/";
        Assert.Equal("'<Control><Alt>s'", await Gsettings("get", binding, "binding"));
        Assert.Equal("'\"/opt/scan wise/Deckle\" --scan'", await Gsettings("get", binding, "command"));

        // Changing the key updates the same entry instead of adding another.
        await LinuxDesktop.SetShortcutAsync("/opt/scan wise/Deckle", 'K');
        Assert.Equal("'<Control><Alt>k'", await Gsettings("get", binding, "binding"));
        Assert.Equal(2, (await Gsettings("get", schema, "custom-keybindings")).Split(',').Length);

        await LinuxDesktop.RemoveShortcutAsync();
        Assert.Equal("['/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/']", await Gsettings("get", schema, "custom-keybindings"));
        Assert.Equal("''", await Gsettings("get", binding, "binding"));
    }

    private static async Task<string> Gsettings(params string[] args)
    {
        var process = Process.Start(new ProcessStartInfo("gsettings", args) { RedirectStandardOutput = true })!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output.Trim();
    }

    [AvaloniaFact]
    public async Task ScanShortcutWaitsForTheScannerThenScans()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SANE_CONFIG_DIR")))
        {
            return; // SANE test backend not enabled (see scripts/test.sh)
        }
        using var services = new AppServices(new AppPaths(Path.Combine(_root, "config"), Path.Combine(_root, "data")));
        services.Settings.SaveFolder = Path.Combine(_root, "out");
        services.Settings.AutoRotate = false;
        var vm = new MainViewModel(services, new FakeUi());
        // Only the virtual test scanner may be picked, never a real device on the network.
        vm.ScanOptions.Settings.DeviceId = "test:0";
        vm.ScanOptions.Settings.Dpi = 50;
        vm.Section = Section.Settings;

        // Requested before the app has even started looking for scanners, like "Deckle --scan".
        var scan = vm.ScanFromShortcutAsync();
        var init = vm.InitializeAsync();
        var start = Environment.TickCount64;
        while (!scan.IsCompleted && Environment.TickCount64 - start < 120_000)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        await scan;
        Assert.Equal("test:0", vm.ScanOptions.SelectedScanner?.Id);
        Assert.Equal(Section.Scan, vm.Section);
        Assert.True(vm.Pages.Count >= 1);
        Assert.NotEmpty(Directory.GetFiles(services.Settings.SaveFolder));
        while (!init.IsCompleted && Environment.TickCount64 - start < 120_000)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
