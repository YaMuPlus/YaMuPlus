using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using YaMuPlus.Ui;

namespace YaMuPlus;

public class App : Application
{
    /// <summary>
    /// Window icon (title bar, taskbar), Windows only. On macOS Avalonia would put it in the Dock instead of
    /// the bundle's AppIcon.icns (which has margins matching the macOS icon grid).
    /// </summary>
    public static WindowIcon? WindowIcon => OperatingSystem.IsWindows()
        ? new WindowIcon(AssetLoader.Open(new Uri("avares://YaMuPlus/Assets/icon.ico")))
        : null;

    /// <summary>
    /// Windows tray icon: the color cassette (small frames of icon.ico). macOS menu bar: a template
    /// silhouette that macOS tints to match the menu bar (light or dark theme).
    /// </summary>
    public static WindowIcon TrayImage => new(AssetLoader.Open(new Uri(OperatingSystem.IsMacOS()
        ? "avares://YaMuPlus/Assets/tray-mac.png"
        : "avares://YaMuPlus/Assets/icon.ico")));

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // exit only explicitly (MainWindow.OnClosing): save dialogs open without the main window
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new MainWindow();
            desktop.MainWindow.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
