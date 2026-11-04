using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using YaMuPlus.Core;

namespace YaMuPlus.Ui;

/// <summary>One feature in the help: where its menu item is and what it does.</summary>
public record HelpItem(string Where, string What);

/// <summary>Help: the list of features in the window language. Enter and Esc close it.</summary>
public partial class HelpWindow : Window
{
    public HelpWindow() // for the Avalonia designer
    {
        InitializeComponent();
        // focus this window: otherwise on macOS keys go to the previously focused element of the main window
        // (the "?" button), so Esc does not close the help and Enter opens a second one
        Opened += (_, _) => CloseBtn.Focus();
    }

    public HelpWindow(Locale loc) : this()
    {
        Icon = App.WindowIcon;
        Title = TitleText.Text = loc["help_title"];
        // one line per feature: "where|what"
        Items.ItemsSource = loc["help_items"].Split('\n').Select(l => l.Split('|', 2)).Select(p => new HelpItem(p[0], p[1]));
        CloseBtn.Content = loc["btn_close"];
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
