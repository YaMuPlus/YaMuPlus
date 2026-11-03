using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using YaMuPlus.Core;

namespace YaMuPlus.Platform;

/// <summary>macOS: .app bundle (Mach-O + bundle id), pgrep/kill, sh watchdog, dialogs via osascript.</summary>
public sealed class MacPlatform : IPlatform
{
    private const string BundleId = "ru.yandex.desktop.music";

    /// <summary>
    /// The standard name in the Applications folders → any bundle in the Applications folders (renamed, in a subfolder)
    /// → Spotlight by bundle id (anywhere, including other disks). Every candidate is checked by bundle id.
    /// </summary>
    public string? FindApp()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[] { "/Applications", Path.Combine(home, "Applications") }.Where(Directory.Exists).ToArray();
        return roots.Select(r => Resolve(Path.Combine(r, "Яндекс Музыка.app"))).FirstOrDefault(p => p != null)
               ?? AppBundles(roots).Select(Resolve).FirstOrDefault(p => p != null)
               ?? Lines("mdfind", $"kMDItemCFBundleIdentifier == '{BundleId}'").Select(Resolve).FirstOrDefault(p => p != null);
    }

    // *.app in the Applications folders and one level deeper (/Applications/Utilities, vendor folders)
    private static IEnumerable<string> AppBundles(string[] roots)
    {
        var opts = new EnumerationOptions { IgnoreInaccessible = true };
        foreach (var r in roots)
            foreach (var d in Directory.EnumerateDirectories(r, "*", opts))
            {
                if (d.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) { yield return d; continue; }
                foreach (var app in Directory.EnumerateDirectories(d, "*.app", opts)) yield return app;
            }
    }

    public string? Resolve(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Directory.Exists(path)) return ResolveBundle(path);
        // the binary lives in <bundle>/Contents/MacOS/
        try { return IsMachO(path) && IsYandexMusic(Path.GetFullPath(Path.Combine(path, "..", "..", ".."))) ? path : null; }
        catch { return null; }
    }

    /// <summary>
    /// A regular file open dialog (not the "Choose Application" list): starts in Applications, apps only.
    /// Uses AppleScript rather than StorageProvider: an .app is a package folder, and the Avalonia dialog returns nothing for it.
    /// </summary>
    public async Task<string?> ChooseAppAsync(TopLevel owner, string title)
    {
        try
        {
            var p = await Osa($"set a to choose file with prompt \"{Esc(title)}\" of type {{\"com.apple.application-bundle\"}} " +
                              "default location (path to applications folder)\nreturn POSIX path of a", 600_000);
            return string.IsNullOrWhiteSpace(p) ? null : p.Trim().TrimEnd('/');
        }
        catch (OperationCanceledException) { return null; }
    }

    public void KillAll(string exe)
    {
        foreach (var pat in new[] { "^{0}$", "^{0} --remote-debugging-port={1}( |$)" })
            foreach (var pid in Lines("pgrep", "-f", string.Format(pat, Regex.Escape(exe), Launcher.CdpPort)))
                try { Process.Start("kill", "-9 " + pid); } catch { }
        Thread.Sleep(800);
    }

    public Process StartTied(string exe, string args)
    {
        var app = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false })
                  ?? throw new InvalidOperationException("process not started");
        // an sh watchdog per launch: waits for the wrapper to die (survives SIGKILL) and kills the music app;
        // exits by itself if the music app dies first
        var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"while kill -0 {Environment.ProcessId} 2>/dev/null; do kill -0 {app.Id} 2>/dev/null || exit 0; sleep 1; done; kill -9 {app.Id} 2>/dev/null");
        try { Process.Start(psi); } catch { }
        return app;
    }

    public string? AppVersion(string exe)
    {
        // the binary lives in <bundle>/Contents/MacOS/
        var m = Regex.Match(ReadPlist(Path.GetFullPath(Path.Combine(exe, "..", "..", ".."))) ?? "",
            @"<key>CFBundleShortVersionString</key>\s*<string>([^<]+)</string>");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    // ponytail: through osascript, so the notification comes from Script Editor; our own needs a signed bundle with UNUserNotificationCenter
    public void Notify(TopLevel owner, string title, string text) =>
        _ = Task.Run(async () =>
        {
            try { await Osa($"display notification \"{Esc(text)}\" with title \"{Esc(title)}\"", 10_000); }
            catch { }
        });

    public async Task<string?> PickFolderAsync(string title, string start)
    {
        try
        {
            var p = await Osa($"set f to choose folder with prompt \"{Esc(title)}\" default location (POSIX file \"{Esc(start)}\")\n" +
                              "return POSIX path of f");
            return p.Length > 1 ? p.TrimEnd('/') : p;
        }
        catch (OperationCanceledException) { return null; }
    }

    // ── bundle checks ──

    private static bool IsMachO(string path)
    {
        try
        {
            var b = new byte[4];
            using (var f = File.OpenRead(path)) if (f.Read(b, 0, 4) != 4) return false;
            var m = BitConverter.ToUInt32(b);
            // FEEDFACE/FEEDFACF in both byte orders + CAFEBABE (universal)
            return m is 0xCEFAEDFE or 0xCFFAEDFE or 0xFEEDFACE or 0xFEEDFACF or 0xBEBAFECA;
        }
        catch { return false; }
    }

    private static string? ReadPlist(string bundle)
    {
        try { return File.ReadAllText(Path.Combine(bundle, "Contents", "Info.plist")); }
        catch { return null; }
    }

    /// <summary>Strictly by bundle id.</summary>
    private static bool IsYandexMusic(string bundle) =>
        ReadPlist(bundle) is { } xml
        && xml.Contains("<key>CFBundleIdentifier</key>")
        && xml.Contains($"<string>{BundleId}</string>");

    /// <summary>.app → CFBundleExecutable, otherwise the first Mach-O in Contents/MacOS.</summary>
    private static string? ResolveBundle(string bundle)
    {
        var macos = Path.Combine(bundle, "Contents", "MacOS");
        if (!Directory.Exists(macos) || !IsYandexMusic(bundle)) return null;
        var m = Regex.Match(ReadPlist(bundle) ?? "", @"<key>CFBundleExecutable</key>\s*<string>([^<]+)</string>");
        if (m.Success)
        {
            var exe = Path.Combine(macos, m.Groups[1].Value.Trim());
            if (IsMachO(exe)) return exe;
        }
        return Directory.GetFiles(macos).FirstOrDefault(IsMachO);
    }

    // ── processes and AppleScript ──

    /// <summary>Output lines of a command (pgrep, mdfind); an empty list if it fails to start.</summary>
    private static string[] Lines(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return o.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>osascript; the Cancel button and a timeout → OperationCanceledException, a script error → InvalidOperationException.</summary>
    private static async Task<string> Osa(string script, int timeoutMs = 900_000)
    {
        var psi = new ProcessStartInfo("osascript")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(); } catch { } throw; }
        if (p.ExitCode != 0)
        {
            // -128 is Cancel in the dialog; anything else is a real error and must not pass for a cancel
            var err = (await stderr).Trim();
            if (err.Contains("(-128)")) throw new OperationCanceledException();
            throw new InvalidOperationException("osascript: " + err);
        }
        return (await stdout).Trim();
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
