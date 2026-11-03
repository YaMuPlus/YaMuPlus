using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YaMuPlus.Core;

/// <summary>
/// Every 3 s checks the music-application pages and injects the payload if it is missing
/// (page reload) or has a different version. The page language is fixed when the music app starts:
/// re-injecting would reset the page state, including a save in progress, so a language change
/// in the wrapper window reaches the music app only after it restarts. A failed payload run
/// (the app's DI is not ready yet) is retried on the next tick.
/// </summary>
public sealed class Injector : IDisposable
{
    private static readonly string Payload = ReadPayload();
    private static readonly string Version = Regex.Match(Payload, @"const VERSION = (\d+);").Groups[1].Value;

    private readonly Func<Locale> _loc;
    private readonly string _svcUrl, _svcToken;
    private readonly Log _log;
    private Timer? _timer;
    private int _busy;
    private string? _lastError;
    private int _fails;
    private Locale? _pageLoc;

    public event Action? Injected;

    /// <summary>
    /// YaMu+ cannot work with the music app: the payload keeps failing to install, or the page reports a problem it found
    /// itself (our menu item did not appear, a self-check failed). null when it works again. Raised once per distinct text,
    /// from any thread.
    /// </summary>
    public event Action<string?>? Problem;

    /// <summary>A save or delete operation in the page failed: the text of its first error. Once per operation, from any thread.</summary>
    public event Action<string>? SaveFailed;

    // ponytail: 10 failed installs in a row (30 s) mean the app version is not supported; the page needs seconds after start
    private const int FailAfter = 10;
    private int _payloadFails;
    private string? _problem, _saveErrorId;

    /// <summary>The bridge in the music app's main process is installed and writes the files; otherwise the wrapper service does.</summary>
    public bool MainFs { get; set; }

    public Injector(Func<Locale> loc, string svcUrl, string svcToken, Log log)
    {
        _loc = loc;
        _svcUrl = svcUrl;
        _svcToken = svcToken;
        _log = log;
    }

    /// <param name="pageLoc">Page language for the whole lifetime of this music app run.</param>
    public void Start(Locale pageLoc)
    {
        _pageLoc = pageLoc;
        _timer?.Dispose();
        _timer = new Timer(_ => _ = Tick(), null, 0, 3000);
    }

    private async Task Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return; // the previous tick is still running
        try
        {
            var loc = _pageLoc!;
            foreach (var ws in await Cdp.PagesAsync())
            {
                // installed payload: [version:lang, problem it found itself, first error of the latest operation] or null
                var state = await Cdp.EvalAsync(ws, "window.__ymSave ? [window.__ymSave.version + ':' + window.__ymSave.lang, "
                    + "window.__ymSave.problem || null, window.__ymSave.saveError || null] : null", 8000);
                if (PageState(state) is (var key, var pageProblem, var saveError) && key == $"{Version}:{loc.Lang}")
                {
                    SetProblem(pageProblem);
                    if (saveError is (var id, var text) && id != _saveErrorId)
                    {
                        _saveErrorId = id;
                        SaveFailed?.Invoke(text);
                    }
                    continue;
                }
                var svc = JsonSerializer.Serialize(new { url = _svcUrl, token = _svcToken, main = MainFs });
                var res = await Cdp.EvalAsync(ws, Payload.Replace("__LOCALE__", loc.Json).Replace("__SVC__", svc), 30000);
                if (IsOk(res))
                {
                    _lastError = null;
                    _fails = _payloadFails = 0;
                    _log.Info(_loc().F("log_inject", ("v", Version))); // the log uses the window language
                    SetProblem(null);
                    Injected?.Invoke();
                }
                else
                {
                    Report(res ?? "no result");
                    // only the payload's own failures count: no connection to CDP means the music app is not running
                    if (++_payloadFails >= FailAfter) SetProblem(ErrorOf(res));
                }
            }
        }
        catch (Exception e) { Report(e.Message); }
        finally { Volatile.Write(ref _busy, 0); }
    }

    private void SetProblem(string? problem)
    {
        if (problem == _problem) return;
        _problem = problem;
        Problem?.Invoke(problem);
    }

    private static (string? Key, string? Problem, (string Id, string Text)? SaveError)? PageState(string? json)
    {
        try
        {
            using var d = JsonDocument.Parse(json ?? "null");
            var r = d.RootElement;
            if (r.ValueKind != JsonValueKind.Array || r.GetArrayLength() < 3) return null;
            var se = r[2];
            return (r[0].GetString(), r[1].ValueKind == JsonValueKind.String ? r[1].GetString() : null,
                se.ValueKind == JsonValueKind.Object ? (se.GetProperty("id").ToString(), se.GetProperty("text").ToString()) : null);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    /// <summary>The error text of a failed payload result ({ ok: false, error }), otherwise the raw result.</summary>
    private static string ErrorOf(string? json)
    {
        try
        {
            using var d = JsonDocument.Parse(json ?? "null");
            if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("error", out var e)) return e.ToString();
        }
        catch (JsonException) { }
        return json ?? "no result";
    }

    private static bool IsOk(string? json)
    {
        try
        {
            using var d = JsonDocument.Parse(json ?? "null");
            return d.RootElement.ValueKind == JsonValueKind.Object
                   && d.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// For the first seconds after launch or reload the page is not ready yet and failures are normal:
    /// only errors that persist for 5 attempts (15 s) are logged, and only when the text changes.
    /// </summary>
    private void Report(string error)
    {
        if (++_fails < 5 || error == _lastError) return;
        _lastError = error;
        _log.Info("inject: " + error);
    }

    private static string ReadPayload()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("YaMuPlus.payload.js")
                      ?? throw new InvalidOperationException("payload.js resource missing");
        return new StreamReader(s).ReadToEnd();
    }

    public void Dispose() => _timer?.Dispose();
}
