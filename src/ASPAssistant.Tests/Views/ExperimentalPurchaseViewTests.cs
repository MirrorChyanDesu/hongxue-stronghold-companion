using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class ExperimentalPurchaseViewTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var folder = Path.Combine(Path.GetTempPath(), "purchase-toggle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var settings = new SettingsManager(folder);
        var calculator = new CovenantCalculator([], [], new Dictionary<string, string>());
        var panel = new SidePanelWindow(new(), new(), new(), new(), new(), new(),
            new(new LineupStore(Path.Combine(folder, "lineups"), calculator.EquipmentRules), calculator),
            new(calculator.EquipmentRules, Path.Combine(folder, "share")), [], [], [], settings, theme, new() { FreeMovement = true })
        { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Width = 280, Height = 1000 };
        var events = new List<bool>();
        panel.AutomaticPurchaseTrackingChanged += events.Add;
        panel.InitAutomaticPurchaseTracking(Task.Run(settings.LoadAutomaticPurchaseTrackingAsync).GetAwaiter().GetResult());
        try
        {
            panel.Show(); ThemeManager.Apply(theme, night); Pump();
            ((Button)panel.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var toggle = (CheckBox)panel.FindName("AutomaticPurchaseTrackingToggle");
            var peer = (CheckBox)panel.FindName("MultiLineupTrackingToggle");
            toggle.IsChecked.Should().BeFalse(); events.Should().BeEmpty();
            toggle.Style.Should().BeSameAs(peer.Style);
            System.Windows.Automation.AutomationProperties.GetName(toggle).Should().Be("自动标记购买（实验性）");
            foreach (var width in new[] { 280, 360, 400 })
            {
                panel.Width = width;
                foreach (var enabled in new[] { true, false })
                {
                    toggle.IsChecked = enabled;
                    var until = DateTime.UtcNow.AddSeconds(5);
                    while (!toggle.IsEnabled && DateTime.UtcNow < until) Pump();
                    toggle.IsEnabled.Should().BeTrue();
                    Task.Run(settings.LoadAutomaticPurchaseTrackingAsync).GetAwaiter().GetResult().Should().Be(enabled);
                    events.Last().Should().Be(enabled);
                    toggle.BringIntoView(); Pump();
                    toggle.ActualWidth.Should().BeApproximately(peer.ActualWidth, .5);
                    toggle.ActualHeight.Should().BeApproximately(peer.ActualHeight, .5);
                    var row = (DockPanel)VisualTreeHelper.GetParent(toggle);
                    foreach (var text in Desc<TextBlock>(row))
                    {
                        var bounds = text.TransformToAncestor(row).TransformBounds(new Rect(text.RenderSize));
                        bounds.Right.Should().BeLessThanOrEqualTo(row.ActualWidth + .5);
                    }
                    Save(row, theme, night, width, enabled);
                }
            }
            events.Should().Equal(true, false, true, false, true, false);
        }
        finally { panel.Close(); Directory.Delete(folder, true); }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Desc<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var nested in Desc<T>(child)) yield return nested;
        }
    }
    private static void Save(FrameworkElement view, GameShopTheme theme, bool night, int width, bool enabled)
    {
        var dir = Path.Combine(TestOutput.Directory, "experimental-purchases"); Directory.CreateDirectory(dir);
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(240) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
        var root = (FrameworkElement)Window.GetWindow(view)!.Content;
        root.UpdateLayout();
        var full = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        full.Render(root);
        var bounds = view.TransformToAncestor(root).TransformBounds(new Rect(view.RenderSize));
        var bitmap = new CroppedBitmap(full, new Int32Rect((int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top),
            (int)Math.Ceiling(bounds.Right) - (int)Math.Floor(bounds.Left), (int)Math.Ceiling(bounds.Bottom) - (int)Math.Floor(bounds.Top)));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(dir, $"{theme}-{night}-{width}-{enabled}.png")); encoder.Save(file);
    }
}
