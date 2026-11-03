using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace YaMuPlus.Core;

/// <summary>
/// One instance per user: the first one holds a lock file in the data folder (flock on macOS, released even
/// on a crash) and listens on a named pipe. A repeated launch sends its build over the pipe: if it is the same
/// or older, the running instance shows its window and the new one exits; if it is newer, the running instance
/// closes (together with its music app) and the new one waits for the lock and carries on. Settings are shared (config.json).
/// </summary>
public static class SingleInstance
{
    private static readonly string Pipe = "YaMuPlus-" + Environment.UserName;
    private static FileStream? _lock;

    private static readonly Assembly Asm = typeof(SingleInstance).Assembly;

    /// <summary>Build: version and build number (Unix time of the build) from the csproj; with equal versions the larger number is newer.</summary>
    public static readonly (Version Ver, long Num) Build = (Asm.GetName().Version!,
        long.Parse(Asm.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "BuildNumber").Value!));

    /// <summary>This launch replaced an older build, which it closed.</summary>
    public static bool ReplacedOld { get; private set; }

    /// <summary>true: carry on; false: exit, the running build is not older than ours and has already shown its window.</summary>
    public static bool Acquire()
    {
        if (TryLock()) return true;
        if (Ask() != "bye") return false;
        // the running build is older and is closing: wait for its lock
        for (var i = 0; i < 100; i++)
        {
            Thread.Sleep(100);
            if (TryLock()) return ReplacedOld = true;
        }
        return false;
    }

    private static bool TryLock()
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            _lock = new FileStream(Path.Combine(Settings.Dir, "instance.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException) { return false; } // the lock is taken
        catch { return true; } // the folder is unavailable: do not block the launch
    }

    /// <summary>Sends our build to the running instance; the reply is "bye" (it is closing) or "shown"; null if there is no reply.</summary>
    private static string? Ask()
    {
        try
        {
            // the user launched us again: let the running instance bring its window to the foreground
            if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(-1);
            using var c = new NamedPipeClientStream(".", Pipe, PipeDirection.InOut);
            c.Connect(3000); // the running instance may not have opened the pipe yet
            var w = new StreamWriter(c) { AutoFlush = true };
            w.WriteLine(Format(Build));
            return new StreamReader(c).ReadLine();
        }
        catch { return null; }
    }

    /// <summary>Waits for repeated launches: onShow shows the window, onReplaced closes the app (a newer build was started).</summary>
    public static void Listen(Action onShow, Action onReplaced) => Task.Run(async () =>
    {
        while (true)
            try
            {
                await using var s = new NamedPipeServerStream(Pipe, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await s.WaitForConnectionAsync();
                var line = await new StreamReader(s).ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));
                var newer = Parse(line) is { } b && b.CompareTo(Build) > 0;
                await new StreamWriter(s) { AutoFlush = true }.WriteLineAsync(newer ? "bye" : "shown");
                if (newer) { onReplaced(); return; }
                onShow();
            }
            catch { await Task.Delay(1000); }
    });

    private static string Format((Version Ver, long Num) b) => $"{b.Ver}|{b.Num}";

    private static (Version Ver, long Num)? Parse(string? s) =>
        s?.Split('|') is [var v, var n] && Version.TryParse(v, out var ver) && long.TryParse(n, out var num)
            ? (ver, num)
            : null;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int pid);
}
