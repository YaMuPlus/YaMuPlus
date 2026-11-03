using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using YaMuPlus.Core;
using YaMuPlus.Platform;

namespace YaMuPlus.Ui;

/// <summary>Wrapper window: language, app path, launch/restart, status and log. The logic lives in Core.</summary>
public partial class MainWindow : Window
{
    private readonly IPlatform _os = IPlatform.Current;
    private readonly Log _log = new();
    private readonly Launcher _launcher;
    private Locale _loc = Locale.Load("ru");
    private readonly Task _startupFind;
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _trayShow = new(), _trayQuit = new();

    public MainWindow()
    {
        InitializeComponent();
        Icon = App.WindowIcon;

        // the tray (Windows) / menu bar (macOS) icon exists only while the window is minimized there: a click or Show
        // brings the window back (the icon disappears), Quit acts like closing the window (also stops the music app)
        _trayShow.Click += (_, _) => ShowFromTray();
        _trayQuit.Click += (_, _) => Close();
        var menu = new NativeMenu();
        menu.Items.Add(_trayShow);
        menu.Items.Add(_trayQuit);
        _tray = new TrayIcon { Icon = App.TrayImage, ToolTipText = "YaMu+", Menu = menu, IsVisible = false };
        if (OperatingSystem.IsMacOS()) MacOSProperties.SetIsTemplateIcon(_tray, true);
        _tray.Clicked += (_, _) => ShowFromTray();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
        // macOS: a click on the Dock icon while the window is hidden also brings the window back
        if (Application.Current?.TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            activatable.Activated += (_, e) => { if (e.Kind == ActivationKind.Reopen) ShowFromTray(); };
        // the program was launched again: the same or an older build shows this window, a newer one takes over
        SingleInstance.Listen(() => Dispatcher.UIThread.Post(ShowFromTray), () => Dispatcher.UIThread.Post(Close));
        _launcher = new Launcher(_os, () => _loc, _log);
        _log.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            LogText.Text = _log.Text();
            LogScroll.ScrollToEnd();
        }, DispatcherPriority.Background);
        _launcher.Status += SetStatus;
        _launcher.RunningChanged += () => Dispatcher.UIThread.Post(UpdateLaunchState);
        _launcher.SaveModeFellBack += () => Dispatcher.UIThread.Post(() => SaveBox.SelectedIndex = 1);
        _launcher.HookProblem += problem => Dispatcher.UIThread.Post(() => OnHookProblem(problem));
        _launcher.SaveFailed += () => Dispatcher.UIThread.Post(() => Alert(_loc["st_save_failed"]));

        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);
        AddHandler(DragDrop.DropEvent, (_, e) => SetPicked(e.Data.GetFiles()?.FirstOrDefault()?.TryGetLocalPath()));

        // settings are shared by all versions: the saved path if it still leads to the music app, otherwise a search
        if (_os.Resolve(Settings.AppPath) != null) PathBox.Text = Settings.AppPath;
        _startupFind = FindAsync(quiet: true); // silently at startup, only if the field is empty
        TrayLaunchBox.IsChecked = Settings.TrayOnLaunch;
        LangBox.ItemsSource = new[] { "Русский", "English" };
        LangBox.SelectedIndex = Math.Max(0, Array.IndexOf(Locale.Languages, Settings.Lang)); // → OnLangChanged
        StatusText.Text = _loc["st_ready"];
        if (SingleInstance.ReplacedOld) _log.Info(_loc["log_replaced"]);
    }

    private bool _wasOpened;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_wasOpened) return; // Opened fires every time the window is shown, including from the tray
        _wasOpened = true;
        if (Environment.GetCommandLineArgs().Contains("--launch")) // self-test flag: press Launch right away
            Dispatcher.UIThread.Post(async () => { await _startupFind; OnLaunch(null, new RoutedEventArgs()); });
    }

    private void OnLangChanged(object? sender, SelectionChangedEventArgs e)
    {
        _loc = Locale.Load(Locale.Languages[Math.Max(0, LangBox.SelectedIndex)]);
        Settings.Lang = _loc.Lang;
        Title = TitleText.Text = _loc["app_title"];
        // the version comes from <Version> in the csproj; the build number tells builds of one version apart
        VersionText.Text = _loc.F("app_version", ("v", SingleInstance.Build.Ver.ToString(3)),
            ("build", SingleInstance.Build.Num.ToString()));
        LangLabel.Text = _loc["app_lang"];
        SaveLabel.Text = _loc["app_save"];
        _langSwitch = true; // the list items are recreated; this is not a user choice
        SaveBox.ItemsSource = new[] { _loc["save_via_app"], _loc["save_via_wrapper"] };
        SaveBox.SelectedIndex = Settings.SaveViaApp ? 0 : 1;
        _langSwitch = false;
        PathLabel.Text = _loc["app_path"];
        BrowseBtn.Content = _loc["btn_browse"];
        FindBtn.Content = _loc["btn_find"];
        PathBox.Watermark = _loc["path_hint"];
        QuitBtn.Content = _trayQuit.Header = _loc["btn_quit"];
        TrayBtn.Content = _loc[OperatingSystem.IsMacOS() ? "btn_tray_mac" : "btn_tray"];
        TrayLaunchBox.Content = _loc[OperatingSystem.IsMacOS() ? "chk_tray_launch_mac" : "chk_tray_launch"];
        _trayShow.Header = _loc["tray_show"];
        foreach (var (btn, key) in new[] { (HelpBtn, "tip_help"), (RepoBtn, "tip_github") })
        {
            ToolTip.SetTip(btn, _loc[key]);
            AutomationProperties.SetName(btn, _loc[key]); // an icon button has no text: give screen readers a name
        }
        UpdateLaunchState();
        if (_launcher.IsRunning) SetStatus(_loc["st_lang_restart"]); // the music app page keeps the language it was started with
    }

    private const string RepoUrl = "https://github.com/YaMuPlus/YaMuPlus";

    private async void OnHelp(object? sender, RoutedEventArgs e) => await new HelpWindow(_loc).ShowDialog(this);

    private async void OnRepo(object? sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(new Uri(RepoUrl));

    private void OnToTray(object? sender, RoutedEventArgs e) => ToTray();

    private void ToTray()
    {
        _tray.IsVisible = true;
        Hide();
    }

    private void OnTrayLaunchChanged(object? sender, RoutedEventArgs e) => Settings.TrayOnLaunch = TrayLaunchBox.IsChecked == true;

    private void ShowFromTray()
    {
        _tray.IsVisible = false;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private bool _langSwitch;

    /// <summary>Who writes the files: the music app or YaMu+. Applied when the music app starts, so tell the user if it is running.</summary>
    private void OnSaveModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_langSwitch || SaveBox.SelectedIndex < 0 || (SaveBox.SelectedIndex == 0) == Settings.SaveViaApp) return;
        Settings.SaveViaApp = SaveBox.SelectedIndex == 0;
        if (_launcher.IsRunning) SetStatus(_loc["st_mode_restart"]);
    }

    /// <summary>Path from the field (quotes come from Explorer's "Copy as path"); Exe resolves it to the executable or null.</summary>
    private string RawPath => PathBox.Text?.Trim().Trim('"') ?? "";

    private string? Exe => _os.Resolve(RawPath);

    private void UpdateLaunchState()
    {
        var exe = Exe;
        LaunchBtn.Content = _loc[_launcher.IsRunning ? "btn_restart" : "btn_launch"];
        LaunchBtn.IsEnabled = exe != null;
        // a path that does not lead to a suitable app shows an error right at the field; an empty field is not an error
        var bad = exe == null && RawPath.Length > 0;
        PathBox.Classes.Set("error", bad);
        PathError.IsVisible = bad;
        // a missing file, the Microsoft Store version and any other wrong file get different messages
        PathError.Text = _loc[_os.IsStoreApp(RawPath) ? "st_store_unsupported"
            : File.Exists(RawPath) || Directory.Exists(RawPath) ? "st_not_ym" : "st_path_missing"];
        StatusDot.Classes.Set("on", _launcher.IsRunning);
    }

    private void OnPathChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateLaunchState();
        if (Exe != null) Settings.AppPath = RawPath; // remember a suitable path for later launches
    }

    private void SetStatus(string text) => Dispatcher.UIThread.Post(() => StatusText.Text = text);

    /// <summary>Hooking into the music app failed, or works again (null).</summary>
    private void OnHookProblem(string? problem)
    {
        if (problem == null) StatusText.Text = _loc["st_done"];
        else Alert(_loc.F("st_hook_failed", ("ver", _launcher.AppVersion ?? "?")));
    }

    /// <summary>The status says it; a hidden window (tray, menu bar) also shows a system notification.</summary>
    private void Alert(string text)
    {
        StatusText.Text = text;
        if (!IsVisible || WindowState == WindowState.Minimized) _os.Notify(this, "YaMu+", text);
    }

    /// <summary>Chosen in the dialog or dropped: puts the choice into the field (a suitable bundle or shortcut becomes the executable).</summary>
    private void SetPicked(string? path)
    {
        if (!string.IsNullOrEmpty(path)) PathBox.Text = _os.Resolve(path) ?? path;
    }

    private async void OnFind(object? sender, RoutedEventArgs e) => await FindAsync(quiet: false);

    /// <summary>Finds the installed app in the background. quiet is for startup: no messages and the typed path stays untouched.</summary>
    private async Task FindAsync(bool quiet)
    {
        FindBtn.IsEnabled = false;
        if (!quiet) StatusText.Text = _loc["st_finding"];
        try
        {
            var (exe, storeOnly) = await Task.Run(() => { var e = _os.FindApp(); return (e, e == null && _os.HasStoreApp()); });
            if (quiet && !string.IsNullOrWhiteSpace(PathBox.Text)) return;
            if (exe != null) PathBox.Text = exe;
            // only the Microsoft Store version is installed: say so even at startup, otherwise it is unclear why nothing is found
            if (storeOnly) StatusText.Text = _loc["st_store_only"];
            else if (!quiet) StatusText.Text = _loc[exe != null ? "st_found" : "st_not_found"];
        }
        finally { FindBtn.IsEnabled = true; }
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        try
        {
            var p = await _os.ChooseAppAsync(this, _loc["dlg_choose_app"]);
            if (p != null) SetPicked(p);
        }
        catch (Exception ex) { SetStatus(ex.Message); } // async void: otherwise an exception crashes the app
    }

    private async void OnLaunch(object? sender, RoutedEventArgs e)
    {
        var exe = Exe;
        if (exe == null) { SetStatus(_loc["st_not_ym"]); return; }
        LaunchBtn.IsEnabled = false;
        try { await _launcher.LaunchAsync(exe); }
        finally { UpdateLaunchState(); }
        if (Settings.TrayOnLaunch && _launcher.IsRunning) ToTray(); // the "minimize when the music app starts" setting
    }

    private void OnQuit(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // closing the wrapper stops the music app; exit outside the native close callback (otherwise SIGABRT on macOS)
        e.Cancel = true;
        Hide();
        _tray.Dispose(); // otherwise on Windows the icon stays in the tray until hovered
        Task.Run(async () =>
        {
            await Task.Delay(250);
            try { _launcher.Dispose(); } catch { }
            Environment.Exit(0);
        });
    }
}
