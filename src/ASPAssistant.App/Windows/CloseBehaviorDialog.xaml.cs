using System.Windows;
using System.Windows.Input;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Windows;

public partial class CloseBehaviorDialog : Window
{
    public MainWindowCloseBehavior? Choice { get; private set; }
    public bool Remember => RememberChoice.IsChecked == true;

    public CloseBehaviorDialog() {
        InitializeComponent();
        WindowUiScale.PrepareCentered(this);
    }

    private void OnExit(object sender, RoutedEventArgs e) => Choose(MainWindowCloseBehavior.Exit);
    private void OnTray(object sender, RoutedEventArgs e) => Choose(MainWindowCloseBehavior.MinimizeToTray);
    private void Choose(MainWindowCloseBehavior behavior)
    {
        Choice = behavior;
        DialogResult = true;
    }
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        DialogResult = false;
    }
    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
