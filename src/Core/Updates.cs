using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace YaMuPlus.Core;

/// <summary>The latest release on GitHub: its version (from the v1.2.3 tag) and its page.</summary>
public static class Updates
{
    private const string Latest = "https://github.com/YaMuPlus/YaMuPlus/releases/latest";

    /// <summary>
    /// releases/latest redirects to the page of the latest release, releases/tag/v1.2.3. The GitHub API would give the
    /// same, but it allows 60 requests an hour per IP address, which users behind a shared VPN address run out of.
    /// </summary>
    public static async Task<(Version Ver, string Page)> LatestAsync()
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        var latest = new Uri(Latest);
        using var r = await http.GetAsync(latest, HttpCompletionOption.ResponseHeadersRead);
        var page = r.Headers.Location is { } to ? new Uri(latest, to) : throw new HttpRequestException($"HTTP {(int)r.StatusCode}");
        return (Version.Parse(page.Segments[^1].TrimStart('v')), page.ToString());
    }
}
