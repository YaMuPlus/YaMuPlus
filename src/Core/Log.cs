using System;
using System.Collections.Generic;

namespace YaMuPlus.Core;

/// <summary>Thread-safe log: ring buffer of recent lines + an event for the UI.</summary>
public sealed class Log
{
    private const int Capacity = 400;
    private readonly Queue<string> _lines = new();

    public event Action? Changed;

    public void Info(string msg)
    {
        lock (_lines)
        {
            _lines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {msg}");
            if (_lines.Count > Capacity) _lines.Dequeue();
        }
        Changed?.Invoke();
    }

    public string Text()
    {
        lock (_lines) return string.Join("\n", _lines);
    }
}
