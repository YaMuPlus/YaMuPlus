using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace YaMuPlus.Core;

/// <summary>
/// Wrapper data folder (config.json, crash.log):
/// macOS: ~/Library/Application Support/YaMuPlus, Windows: %LOCALAPPDATA%\YaMuPlus.
/// </summary>
public static class Settings
{
    public static string Dir { get; } = Path.Combine(OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YaMuPlus");

    private static string ConfigPath => Path.Combine(Dir, "config.json");

    /// <summary>Value of a config.json key (a string, or true/false as text); null if the file or key is missing.</summary>
    private static string? Get(string key)
    {
        try { return JsonNode.Parse(File.ReadAllText(ConfigPath))?[key]?.ToString(); }
        catch { return null; }
    }

    /// <summary>Writes one key and keeps the other settings as they were.</summary>
    private static void Set(string key, JsonNode? value)
    {
        try
        {
            JsonObject cfg;
            try { cfg = JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject ?? new JsonObject(); }
            catch { cfg = new JsonObject(); }
            cfg[key] = value;
            Directory.CreateDirectory(Dir);
            File.WriteAllText(ConfigPath, cfg.ToJsonString());
        }
        catch { }
    }

    /// <summary>Files are written by the music app's main process (default) or by the wrapper service; switch in the window.</summary>
    public static bool SaveViaApp
    {
        get => Get("saveVia") != "wrapper";
        set => Set("saveVia", value ? "app" : "wrapper");
    }

    /// <summary>Language of the window and the music app page (a code from <see cref="Locale.Languages"/>); null if never chosen.</summary>
    public static string? Lang
    {
        get => Get("lang");
        set => Set("lang", value);
    }

    /// <summary>The last music app path from the window's field that led to a suitable app.</summary>
    public static string? AppPath
    {
        get => Get("appPath");
        set => Set("appPath", value);
    }

    /// <summary>After the music app starts, minimize the wrapper window to the tray (the menu bar on macOS).</summary>
    public static bool TrayOnLaunch
    {
        get => Get("trayOnLaunch") == "true";
        set => Set("trayOnLaunch", value);
    }

    /// <summary>
    /// A file or folder name valid on the current OS: forbidden characters → "_", no trailing dots
    /// or spaces (Windows silently strips them), no empty names and no "." or "..".
    /// </summary>
    public static string SafeName(string? s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var r = new string((s ?? "").Select(c => Array.IndexOf(bad, c) >= 0 ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
        return r.Length == 0 ? "_" : r;
    }

    public static void Crash(string kind, object? ex)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Path.Combine(Dir, "crash.log"), $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}: {ex}\n");
        }
        catch { }
    }
}
