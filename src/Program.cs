using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using YaMuPlus.Core;

namespace YaMuPlus;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Settings.Crash("Unhandled", e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { Settings.Crash("Unobserved", e.Exception); e.SetObserved(); };
        if (!SingleInstance.Acquire()) return 0; // a build no older than this one is already running and has shown its window
        Task.Run(CleanOldExtractions);
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            Settings.Crash("Main", ex);
            return 1;
        }
    }

    /// <summary>
    /// On Windows a single-file build extracts native libraries to %TEMP%\.net\YaMuPlus\&lt;build hash&gt; and never removes
    /// the folders of previous versions. Delete all but the current one. The macOS bundle ships them next to the binary.
    /// </summary>
    private static void CleanOldExtractions()
    {
        if (!OperatingSystem.IsWindows()) return;
        var app = Path.Combine(Path.GetTempPath(), ".net", "YaMuPlus");
        // the current extraction folder is among the native library search paths
        var keep = (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.TrimEndingDirectorySeparator(Path.GetFullPath(d)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // it must be found; if it is not (not running from a single-file build), touch nothing
        if (!keep.Any(k => k.StartsWith(app, StringComparison.OrdinalIgnoreCase))) return;
        foreach (var d in Directory.GetDirectories(app))
            if (!keep.Contains(Path.TrimEndingDirectorySeparator(Path.GetFullPath(d))))
                try { Directory.Delete(d, true); } catch { } // in use by another running instance: next time
    }

    // also used by the Avalonia designer
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
