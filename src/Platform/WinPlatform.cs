using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Win32;
using YaMuPlus.Core;

namespace YaMuPlus.Platform;

/// <summary>
/// Windows: electron-builder installation (%LOCALAPPDATA%\Programs\YandexMusic), check by the
/// exe version resource, a Job Object instead of a watchdog, Avalonia windows for dialogs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinPlatform : IPlatform
{
    private const string Product = "Яндекс Музыка";

    /// <summary>
    /// The Microsoft Store version (UWP on .NET Native, the 4.x line) is recognized by its surroundings, not by the path:
    /// the package manifest next to the file (or in the chosen folder) declares the Yandex Music package.
    /// Its exe has an empty version resource, so the file itself tells nothing.
    /// </summary>
    public bool IsStoreApp(string? path)
    {
        try
        {
            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            var manifest = dir == null ? null : Path.Combine(dir, "AppxManifest.xml");
            if (manifest == null || !File.Exists(manifest)) return false;
            var xml = File.ReadAllText(manifest);
            if (!xml.Contains("Yandex", StringComparison.OrdinalIgnoreCase)) return false; // most packages, without parsing
            var id = XDocument.Parse(xml).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Identity")?.Attribute("Name")?.Value;
            return id != null && id.EndsWith(".Yandex.Music", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Installed packages of the current user with their root folders; each is checked like a chosen file.</summary>
    public bool HasStoreApp()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            foreach (var name in k?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var p = k!.OpenSubKey(name);
                if (p?.GetValue("PackageRootFolder") is string root && IsStoreApp(root)) return true;
            }
        }
        catch { }
        return false;
    }

    public string? AppVersion(string exe)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(exe);
            return $"{v.ProductMajorPart}.{v.ProductMinorPart}.{v.ProductBuildPart}";
        }
        catch { return null; }
    }

    /// <summary>
    /// A balloon of a temporary tray icon of our own: Windows 10/11 shows it as a notification. The icon from
    /// Avalonia's TrayIcon cannot be used, it exposes no balloon API.
    /// </summary>
    public void Notify(TopLevel owner, string title, string text)
    {
        var hwnd = owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;
        ExtractIconEx(Environment.ProcessPath!, 0, out var large, out var small, 1);
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = hwnd, uID = 0x594D, // "YM"
            uFlags = NIF_ICON | NIF_TIP | NIF_INFO, hIcon = small, szTip = title,
            szInfoTitle = title, szInfo = text, dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON, hBalloonIcon = large,
        };
        Shell_NotifyIcon(NIM_DELETE, ref data); // a previous notification that is still there
        Shell_NotifyIcon(NIM_ADD, ref data);
        _ = Task.Delay(15000).ContinueWith(_ =>
        {
            Shell_NotifyIcon(NIM_DELETE, ref data);
            DestroyIcon(small);
            DestroyIcon(large);
        });
    }

    private const int NIM_ADD = 0, NIM_DELETE = 2, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    private const int NIIF_USER = 0x4, NIIF_LARGE_ICON = 0x20;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    /// <summary>Stages from fast to slow; candidates go through the same check as a manual choice.</summary>
    public string? FindApp()
    {
        foreach (var stage in new Func<IEnumerable<string>>[] { StandardPlaces, UninstallEntries, RunningApp, Shortcuts, ProgramFolders })
        {
            List<string> found;
            try { found = stage().ToList(); }
            catch { continue; } // no access to this source: try the next one
            var exe = found.Select(Resolve).FirstOrDefault(p => p != null);
            if (exe != null) return exe;
        }
        return null;
    }

    // electron-builder installer: per user or for all users
    private static IEnumerable<string> StandardPlaces() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "YandexMusic"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "YandexMusic"),
    };

    // the installer entry in "Programs and Features": InstallLocation or DisplayIcon ("…\Яндекс Музыка.exe,0")
    private static IEnumerable<string> UninstallEntries()
    {
        const string key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        var found = new List<string>();
        foreach (var (root, path) in new[] { (Registry.CurrentUser, key), (Registry.LocalMachine, key), (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall") })
        {
            using var k = root.OpenSubKey(path);
            foreach (var name in k?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var e = k!.OpenSubKey(name);
                if (e?.GetValue("DisplayName") is not string dn || !dn.StartsWith(Product, StringComparison.OrdinalIgnoreCase)) continue;
                if (e.GetValue("InstallLocation") is string loc && loc.Length > 0) found.Add(loc);
                if (e.GetValue("DisplayIcon") is string icon) found.Add(icon.Split(',')[0].Trim('"'));
            }
        }
        return found;
    }

    // a running music app: the path of its process
    private static IEnumerable<string> RunningApp()
    {
        var found = new List<string>();
        foreach (var p in Process.GetProcessesByName(Product))
            using (p) try { if (p.MainModule?.FileName is { } f) found.Add(f); } catch { }
        return found;
    }

    // standard program folders: the app may be installed under another folder name, so the exe files in every
    // subfolder are checked (Resolve on a folder goes through its *.exe by version resource)
    private static IEnumerable<string> ProgramFolders()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var opts = new EnumerationOptions { IgnoreInaccessible = true };
        return new[]
            {
                Path.Combine(local, "Programs"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                local,
            }
            .Where(r => r.Length > 0 && Directory.Exists(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(r => Directory.EnumerateDirectories(r, "*", opts));
    }

    // Start menu and desktop shortcuts (current user and all users)
    private static IEnumerable<string> Shortcuts()
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return new[] { Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu,
                       Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory }
            .Select(Environment.GetFolderPath)
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.lnk", opts))
            .Where(f => Path.GetFileName(f).Contains("Музык", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(f).Contains("Music", StringComparison.OrdinalIgnoreCase));
    }

    public string? Resolve(string? path)
    {
        if (string.IsNullOrEmpty(path) || IsStoreApp(path)) return null;
        try
        {
            // a desktop or Start menu shortcut: take its target
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) path = ShortcutTarget(path);
            if (string.IsNullOrEmpty(path)) return null;
            if (Directory.Exists(path)) return Directory.EnumerateFiles(path, "*.exe").FirstOrDefault(IsYandexMusic);
            return IsYandexMusic(path) ? path : null;
        }
        catch { return null; }
    }

    /// <summary>Shortcut target via WScript.Shell; Microsoft Store app shortcuts have none (empty string).</summary>
    private static string? ShortcutTarget(string lnk)
    {
        dynamic? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            return (string)shell!.CreateShortcut(lnk).TargetPath;
        }
        catch { return null; }
        finally { if (shell != null) Marshal.FinalReleaseComObject(shell); }
    }

    /// <summary>
    /// Version resource: ProductName = "Яндекс Музыка". The uninstaller in the same folder has the same
    /// ProductName but an empty InternalName, so both fields are compared.
    /// </summary>
    private static bool IsYandexMusic(string exe)
    {
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe)) return false;
        var v = FileVersionInfo.GetVersionInfo(exe);
        return v.ProductName == Product && v.InternalName == Product;
    }

    public async Task<string?> ChooseAppAsync(TopLevel owner, string title)
    {
        var sp = owner.StorageProvider;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("*.exe") { Patterns = new[] { "*.exe" } } },
            SuggestedStartLocation = await sp.TryGetFolderFromPathAsync(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public void KillAll(string exe)
    {
        var full = Path.GetFullPath(exe);
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            using (p)
            {
                try
                {
                    if (!string.Equals(p.MainModule?.FileName, full, StringComparison.OrdinalIgnoreCase)) continue;
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                }
                catch { } // the process has already exited or belongs to someone else (no access)
            }
        }
    }

    public Process StartTied(string exe, string args)
    {
        EnsureKillOnCloseJob();
        return Process.Start(new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        }) ?? throw new InvalidOperationException("process not started");
    }

    public Task<string?> PickFolderAsync(string title, string start) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // the request comes from the music app window and the wrapper is not in the foreground, so the dialog
            // owner is an invisible topmost window; otherwise the folder picker opens behind the music app window.
            // Its size roughly matches the dialog: Windows places the dialog at the owner's corner, and at 1×1 it went off screen
            var owner = new Window
            {
                Topmost = true, ShowInTaskbar = false, SystemDecorations = SystemDecorations.None,
                Width = 960, Height = 600, Opacity = 0, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            owner.Show();
            owner.Activate();
            try
            {
                var picked = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false,
                    SuggestedStartLocation = await owner.StorageProvider.TryGetFolderFromPathAsync(start),
                });
                return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
            }
            finally { owner.Close(); }
        });

    // ── Job Object: the music app dies together with the wrapper ──
    // The wrapper puts itself into the job; child processes (the music app and all its Electron
    // subprocesses) inherit it without a race. The job handle lives until the wrapper process ends:
    // on any exit (including a crash or ending it from Task Manager) the OS closes the handle
    // and terminates the whole job.

    private static IntPtr _job;

    private static void EnsureKillOnCloseJob()
    {
        if (_job != IntPtr.Zero) return;
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new InvalidOperationException("CreateJobObject failed: " + Marshal.GetLastWin32Error());
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        var len = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, (uint)len)
            || !AssignProcessToJobObject(job, Process.GetCurrentProcess().Handle))
        {
            var err = Marshal.GetLastWin32Error();
            CloseHandle(job);
            throw new InvalidOperationException("Job Object setup failed: " + err);
        }
        _job = job;
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const int JobObjectExtendedLimitInformation = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attrs, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint len);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
