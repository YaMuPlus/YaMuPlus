using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using YaMuPlus.Core;

namespace YaMuPlus.Platform;

/// <summary>Everything OS-specific: finding and checking the app, processes, dialogs.</summary>
public interface IPlatform
{
    static IPlatform Current { get; } = OperatingSystem.IsWindows() ? new WinPlatform() : new MacPlatform();

    /// <summary>
    /// Finds the installed Yandex Music: standard locations first, then system sources (registry, shortcuts,
    /// Spotlight). Returns the executable or null. May take a couple of seconds, so do not call on the UI thread.
    /// </summary>
    string? FindApp();

    /// <summary>A user-supplied path (bundle, folder or executable) → the Yandex Music executable or null.</summary>
    string? Resolve(string? path);

    /// <summary>The path is the Microsoft Store version: a separate UWP app without Electron that YaMu+ cannot work with.</summary>
    bool IsStoreApp(string? path) => false;

    /// <summary>The Microsoft Store version is installed.</summary>
    bool HasStoreApp() => false;

    /// <summary>Version of the music app from its exe or bundle, for the log; null if unknown.</summary>
    string? AppVersion(string exe);

    /// <summary>System notification: a tray balloon on Windows, Notification Center on macOS.</summary>
    void Notify(TopLevel owner, string title, string text);

    /// <summary>System dialog for choosing the app; null on cancel.</summary>
    Task<string?> ChooseAppAsync(TopLevel owner, string title);

    /// <summary>Stops all app instances: Electron holds a single-instance lock, otherwise the CDP flag would not apply.</summary>
    void KillAll(string exe);

    /// <summary>Starts the app tied to the wrapper's lifetime: when the wrapper dies (including a crash), the music app dies too.</summary>
    Process StartTied(string exe, string args);

    /// <summary>
    /// System folder picker for the in-page save dialog (the fallback without the bridge in the music app's
    /// main process): over the music app window, starting at <paramref name="start"/>; null on cancel.
    /// </summary>
    Task<string?> PickFolderAsync(string title, string start);
}
