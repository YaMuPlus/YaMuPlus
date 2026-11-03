using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YaMuPlus.Core;

/// <summary>Minimal CDP client: page list from /json + Runtime.evaluate over WebSocket.</summary>
public static class Cdp
{
    /// <summary>WebSocket URLs of the app pages (music-application://desktop).</summary>
    public static async Task<List<string>> PagesAsync()
    {
        // a new client per call: the music app restarts and breaks pooled connections,
        // and the .NET pool's read-ahead then throws an unobserved exception
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var json = await http.GetStringAsync($"http://127.0.0.1:{Launcher.CdpPort}/json");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Where(t => Str(t, "type") == "page" && Str(t, "url").StartsWith("music-application"))
            .Select(t => Str(t, "webSocketDebuggerUrl"))
            .Where(ws => ws.Length > 0)
            .ToList();
    }

    /// <summary>
    /// WebSocket URL of the Node inspector (Electron started with --inspect) or null. Another process with its own
    /// inspector may hold the port, so the evaluated code itself checks (by PID) that it is the music app we started.
    /// </summary>
    public static async Task<string?> MainAsync(int port)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json/list"));
        return doc.RootElement.EnumerateArray()
            .Where(t => Str(t, "type") == "node")
            .Select(t => Str(t, "webSocketDebuggerUrl")).FirstOrDefault(ws => ws.Length > 0);
    }

    /// <summary>
    /// evaluate (awaiting the promise) → JSON text of the value, or null for undefined.
    /// An exception in the page → <see cref="InvalidOperationException"/> with its text.
    /// </summary>
    public static async Task<string?> EvalAsync(string wsUrl, string expr, int timeoutMs = 20000)
    {
        using var ws = new ClientWebSocket();
        using var cts = new CancellationTokenSource(timeoutMs);
        await ws.ConnectAsync(new Uri(wsUrl), cts.Token);

        const int id = 1;
        var req = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id,
            method = "Runtime.evaluate",
            @params = new { expression = expr, returnByValue = true, awaitPromise = true },
        });
        await ws.SendAsync(req, WebSocketMessageType.Text, true, cts.Token);

        var buf = new byte[64 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await ws.ReceiveAsync(buf, cts.Token);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buf, 0, r.Count);
            if (!r.EndOfMessage) continue;

            using var doc = JsonDocument.Parse(ms.ToArray());
            ms.SetLength(0);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var rid) || rid.GetInt32() != id) continue; // events
            if (!root.TryGetProperty("result", out var res)) return null;
            if (res.TryGetProperty("exceptionDetails", out var ex))
                throw new InvalidOperationException(
                    ex.TryGetProperty("exception", out var e) && e.TryGetProperty("description", out var d)
                        ? d.GetString() : ex.ToString());
            return res.TryGetProperty("result", out var inner) && inner.TryGetProperty("value", out var val)
                ? val.GetRawText() : null;
        }
    }

    private static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
}
