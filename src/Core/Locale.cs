using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace YaMuPlus.Core;

/// <summary>Locale (embedded Assets/locales/*.json) + {placeholder} substitution. Shared by the wrapper and the payload.</summary>
public sealed class Locale
{
    public static readonly string[] Languages = { "ru", "en" };

    private readonly Dictionary<string, string> _map;

    private Locale(Dictionary<string, string> map) => _map = map;

    public static Locale Load(string lang) => new(Read(lang) ?? Read("ru") ?? new Dictionary<string, string>());

    private static Dictionary<string, string>? Read(string lang)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"YaMuPlus.locales.{lang}.json");
        return s == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(s);
    }

    public string Lang => this["lang_code"];

    public string this[string key] => _map.TryGetValue(key, out var v) ? v : key;

    public string F(string key, params (string k, string v)[] vars)
    {
        var s = this[key];
        foreach (var (k, v) in vars) s = s.Replace("{" + k + "}", v);
        return s;
    }

    /// <summary>Locale JSON, substituted into the payload in place of the __LOCALE__ marker.</summary>
    public string Json => JsonSerializer.Serialize(_map);
}
