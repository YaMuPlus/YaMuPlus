using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using YaMuPlus.Platform;

namespace YaMuPlus.Core;

/// <summary>
/// Local service for the payload, the fallback when the bridge in the music app's main process is not installed:
/// /ping and /fs with the same operations as the bridge: start (remembered folder or Downloads), pick (system
/// folder picker), mkdir, write. Nothing is overwritten: a taken name → "Name (2)".
/// Protection: a random session token. Any web page can send requests to 127.0.0.1 but does not know the token
/// (only the injected payload receives it).
/// </summary>
public sealed class SvcServer
{
    public const int Port = 9331;
    public static readonly string Url = $"http://127.0.0.1:{Port}";

    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private readonly IPlatform _os;
    private readonly Log _log;
    private HttpListener? _listener;
    private int _pickBusy;

    public SvcServer(IPlatform os, Log log)
    {
        _os = os;
        _log = log;
    }

    public void Start()
    {
        if (_listener != null) return;
        var ln = new HttpListener();
        ln.Prefixes.Add(Url + "/");
        ln.Start();
        _listener = ln;
        _ = ListenLoop(ln);
        _log.Info($"service :{Port} started");
    }

    public void Stop()
    {
        try { _listener?.Close(); } catch { }
        _listener = null;
    }

    private async Task ListenLoop(HttpListener ln)
    {
        while (ln.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await ln.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        // the payload lives in music-application://desktop, so responses must be readable cross-origin
        res.Headers["Access-Control-Allow-Origin"] = "*";
        res.Headers["Access-Control-Allow-Headers"] = "*";
        res.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
        try
        {
            var path = req.Url!.AbsolutePath;
            if (req.HttpMethod == "OPTIONS") { res.StatusCode = 204; res.Close(); return; }
            if (path == "/ping") { await Reply(res, new { ok = true }); return; }
            if (req.HttpMethod != "POST" || path != "/fs") { res.StatusCode = 404; res.Close(); return; }

            using var doc = await JsonDocument.ParseAsync(req.InputStream);
            var m = doc.RootElement;
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Str(m, "token")), Encoding.UTF8.GetBytes(Token)))
            {
                res.StatusCode = 403;
                await Reply(res, new { ok = false, error = "forbidden" });
                return;
            }
            var a = m.TryGetProperty("args", out var args) ? args : default;
            object? result = Str(m, "op") switch
            {
                "start" => Directory.Exists(Str(a, "path")) ? Str(a, "path") : Downloads(),
                "pick" => await PickAsync(Str(a, "path"), Str(a, "title")),
                "mkdir" => Mkdir(Str(a, "parent"), Str(a, "name")),
                "write" => await WriteAsync(Str(a, "dir"), Str(a, "name"), Convert.FromBase64String(Str(m, "b64"))),
                var op => throw new InvalidOperationException("unknown op " + op),
            };
            await Reply(res, new { ok = true, result });
        }
        catch (Exception e)
        {
            _log.Info("svc err: " + e.Message);
            try { await Reply(res, new { ok = false, error = e.Message }); } catch { }
        }
    }

    private static string Downloads() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>One dialog at a time; a repeated request while it is open counts as a cancel. The title comes
    /// from the page and matches the folder picker in the music app's main process.</summary>
    private async Task<string?> PickAsync(string start, string title)
    {
        if (Interlocked.Exchange(ref _pickBusy, 1) == 1) return null;
        try { return await _os.PickFolderAsync(title, Directory.Exists(start) ? start : Downloads()); }
        finally { Volatile.Write(ref _pickBusy, 0); }
    }

    private static object Mkdir(string parent, string name)
    {
        var n = FreeName(parent, Settings.SafeName(name), file: false);
        var p = Path.Combine(parent, n);
        Directory.CreateDirectory(p);
        return new { path = p, name = n };
    }

    private async Task<object> WriteAsync(string dir, string name, byte[] data)
    {
        var n = FreeName(dir, Settings.SafeName(name), file: true);
        // CreateNew: the file may appear between the name check and the write; then fail instead of overwriting
        await using (var f = new FileStream(Path.Combine(dir, n), FileMode.CreateNew, FileAccess.Write))
            await f.WriteAsync(data);
        _log.Info($"write: {n}");
        return new { name = n };
    }

    /// <summary>The first free "Name.ext", "Name (2).ext"…; a folder has no extension ("Mr. Big (2)").</summary>
    private static string FreeName(string dir, string name, bool file)
    {
        var (stem, ext) = file ? (Path.GetFileNameWithoutExtension(name), Path.GetExtension(name)) : (name, "");
        var n = name;
        for (var i = 2; File.Exists(Path.Combine(dir, n)) || Directory.Exists(Path.Combine(dir, n)); i++) n = $"{stem} ({i}){ext}";
        return n;
    }

    private static string Str(JsonElement m, string key) =>
        m.ValueKind == JsonValueKind.Object && m.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    private static async Task Reply(HttpListenerResponse res, object body)
    {
        res.ContentType = "application/json";
        await res.OutputStream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(body));
        res.Close();
    }
}
