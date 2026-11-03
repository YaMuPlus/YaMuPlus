using System;
using System.Diagnostics;
using System.Threading.Tasks;
using YaMuPlus.Platform;

namespace YaMuPlus.Core;

/// <summary>
/// Lifecycle: starts Yandex Music with CDP (after stopping all its instances),
/// the save service, periodic payload injection, shutdown.
/// </summary>
public sealed class Launcher : IDisposable
{
    public const int CdpPort = 9229;

    /// <summary>Node inspector of the music app's main process: open only while <see cref="FsBridge"/> is being installed.</summary>
    public const int InspectPort = CdpPort + 1;

    /// <summary>
    /// File operations of the save dialog, done in the music app's main process without Chromium's folder restrictions:
    /// start (the remembered folder if any, otherwise Downloads), pick (system folder picker over the music app window),
    /// mkdir, write. The channel is the existing files:save-png IPC (the musicDesktop.files.savePng bridge from preload):
    /// messages prefixed with "yamu:token:" from the music app's own page are intercepted in ipcMain.emit (the order
    /// in which the app registers its handlers does not matter), everything else goes to the regular handler. The reply
    /// goes to window.__yamuFs via executeJavaScript. Nothing is overwritten: a taken name → "Name (2)".
    /// The inspector is closed right after installation.
    /// </summary>
    private const string FsBridge = """
        (() => {
          if (process.pid !== __PID__) return 'foreign'; // another process with its own inspector holds the port
          const m = process.mainModule, e = m.require('electron'), fs = m.require('fs/promises'), path = m.require('path');
          const ch = 'desktop:files:save-png', prefix = 'yamu:__TOKEN__:';
          const isDir = (p) => fs.stat(p).then(s => s.isDirectory(), () => false);
          const free = async (dir, name, file) => { // a folder has no extension: "Mr. Big (2)"
            const ext = file ? path.extname(name) : '', stem = name.slice(0, name.length - ext.length);
            let n = name;
            for (let i = 2; await fs.access(path.join(dir, n)).then(() => true, () => false); i++) n = stem + ' (' + i + ')' + ext;
            return n;
          };
          const ops = {
            async start({ path: p }) { return p && await isDir(p) ? p : e.app.getPath('downloads'); },
            async pick({ path: p, title }, ev) {
              const r = await e.dialog.showOpenDialog(e.BrowserWindow.fromWebContents(ev.sender),
                { title, defaultPath: p, properties: ['openDirectory', 'createDirectory'] });
              return r.canceled || !r.filePaths.length ? null : r.filePaths[0];
            },
            async mkdir({ parent, name }) { const n = await free(parent, name, false), p = path.join(parent, n); await fs.mkdir(p); return { path: p, name: n }; },
            async write({ dir, name }, ev, bytes) { const n = await free(dir, name, true); await fs.writeFile(path.join(dir, n), bytes, { flag: 'wx' }); return { name: n }; },
          };
          const handle = async (ev, msg) => {
            let r = { id: null };
            try { const q = JSON.parse(msg.defaultPath.slice(prefix.length)); r.id = q.id; r.result = await ops[q.op](q.args || {}, ev, msg.buffer); }
            catch (err) { r.error = String(err && err.message || err); }
            ev.sender.executeJavaScript('window.__yamuFs && window.__yamuFs(' + JSON.stringify(r) + ')').catch(() => {});
          };
          const emit = e.ipcMain.emit;
          e.ipcMain.emit = function (name, ev, msg, ...rest) {
            if (name === ch && msg && typeof msg.defaultPath === 'string' && msg.defaultPath.startsWith(prefix)
                && String(ev && ev.senderFrame && ev.senderFrame.url).startsWith('music-application:')) { handle(ev, msg); return true; }
            return emit.call(this, name, ev, msg, ...rest);
          };
          setTimeout(() => m.require('inspector').close(), 200);
          return 'ok';
        })()
        """;

    private readonly IPlatform _os;
    private readonly Func<Locale> _loc;
    private readonly Log _log;
    private readonly SvcServer _svc;
    private readonly Injector _injector;
    private Process? _app;

    /// <summary>Status line for the UI (raised from any thread).</summary>
    public event Action<string>? Status;

    /// <summary>The music app started or exited (raised from any thread).</summary>
    public event Action? RunningChanged;

    /// <summary>The bridge failed and the setting was switched to "via YaMu+" (raised from any thread).</summary>
    public event Action? SaveModeFellBack;

    /// <summary>
    /// YaMu+ could not hook into the music app (the reason and the app version are already in the log), or null when it
    /// works again. Raised from any thread.
    /// </summary>
    public event Action<string?>? HookProblem;

    /// <summary>A save or delete operation in the music app failed (the error and the app version are already in the log).</summary>
    public event Action? SaveFailed;

    /// <summary>Version of the running music app, from its file.</summary>
    public string? AppVersion { get; private set; }

    public bool IsRunning => _app is { HasExited: false };

    public Launcher(IPlatform os, Func<Locale> loc, Log log)
    {
        _os = os;
        _loc = loc;
        _log = log;
        _svc = new SvcServer(os, log);
        _injector = new Injector(loc, SvcServer.Url, _svc.Token, log);
        _injector.Injected += () => Status?.Invoke(_loc()["st_done"]);
        _injector.Problem += problem =>
        {
            if (problem != null) _log.Info(_loc().F("log_hook_failed", ("ver", AppVersion ?? "?"), ("err", problem)));
            HookProblem?.Invoke(problem);
        };
        _injector.SaveFailed += error =>
        {
            _log.Info(_loc().F("log_save_failed", ("ver", AppVersion ?? "?"), ("err", error)));
            SaveFailed?.Invoke();
        };
    }

    public async Task LaunchAsync(string exe)
    {
        var loc = _loc();
        try
        {
            Status?.Invoke(loc[IsRunning ? "st_restart" : "st_launching"]);
            await Task.Run(() =>
            {
                Kill();
                _os.KillAll(exe);
            });

            // via the music app: with the main process inspector (the bridge is installed and the inspector closed right away)
            AppVersion = _os.AppVersion(exe);
            var viaApp = Settings.SaveViaApp;
            var app = _os.StartTied(exe, $"--remote-debugging-port={CdpPort}" + (viaApp ? $" --inspect=127.0.0.1:{InspectPort}" : ""));
            _app = app;
            var pid = app.Id;
            _log.Info(loc.F("log_launch", ("pid", pid.ToString())));
            app.EnableRaisingEvents = true;
            app.Exited += (_, _) =>
            {
                _log.Info(_loc().F("log_kill", ("pid", pid.ToString())));
                if (!ReferenceEquals(_app, app)) return; // the old instance during a restart
                Status?.Invoke(_loc()["st_killed"]);
                RunningChanged?.Invoke();
            };
            RunningChanged?.Invoke();

            _svc.Start();
            Status?.Invoke(loc["st_wait_cdp"]);
            // before the first injection: the payload learns the mode from SVC
            if (viaApp)
            {
                _injector.MainFs = await HookMainAsync(app);
                // failed: switch the setting so the window shows the mode in effect and later launches
                // do not wait for the bridge in vain; the same switch turns it back
                if (!_injector.MainFs) { Settings.SaveViaApp = false; SaveModeFellBack?.Invoke(); }
            }
            else { _injector.MainFs = false; _log.Info(loc["log_fs_wrapper"]); }
            _injector.Start(loc);
        }
        catch (Exception e)
        {
            _log.Info("ERR: " + e.Message);
            Status?.Invoke(e.Message);
        }
    }

    /// <summary>Installs <see cref="FsBridge"/>; if that fails within 10 s, the wrapper service writes the files and the reason is logged.</summary>
    private async Task<bool> HookMainAsync(Process app)
    {
        var err = "timeout";
        for (var until = DateTime.UtcNow.AddSeconds(10); DateTime.UtcNow < until && !app.HasExited; await Task.Delay(500))
        {
            try
            {
                // the inspector comes up before the main module; until then eval fails, so retry
                if (await Cdp.MainAsync(InspectPort) is { } ws
                    && await Cdp.EvalAsync(ws, FsBridge.Replace("__TOKEN__", _svc.Token).Replace("__PID__", app.Id.ToString()), 5000) == "\"ok\"")
                {
                    _log.Info(_loc()["log_fs_hook_ok"]);
                    return true;
                }
                err = "no Electron main process on the port";
            }
            catch (Exception e) { err = e.Message; }
        }
        // the bridge failed but the music app's inspector may be up: do not leave it open for the whole session
        try
        {
            if (await Cdp.MainAsync(InspectPort) is { } ws)
                await Cdp.EvalAsync(ws, $"process.pid === {app.Id} && setTimeout(() => process.getBuiltinModule('inspector').close(), 200)", 3000);
        }
        catch { }
        _log.Info(_loc().F("log_fs_hook_fail", ("err", err)));
        return false;
    }

    /// <summary>Only our own instance. On macOS killing the tree would take down the whole group, wrapper included.</summary>
    public void Kill()
    {
        var app = _app;
        _app = null;
        try
        {
            if (app is { HasExited: false }) app.Kill(entireProcessTree: OperatingSystem.IsWindows());
        }
        catch { }
    }

    public void Dispose()
    {
        _injector.Dispose();
        _svc.Stop();
        Kill();
    }
}
