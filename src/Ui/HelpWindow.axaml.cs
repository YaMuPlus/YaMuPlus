using Avalonia.Controls;
using Avalonia.Interactivity;
using YaMuPlus.Core;

namespace YaMuPlus.Ui;

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
        Items.ItemsSource = loc["help_items"].Split('\n');
        CloseBtn.Content = loc["btn_close"];
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
