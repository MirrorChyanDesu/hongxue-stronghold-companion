using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class CloseBehaviorViewTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var folder = Path.Combine(Path.GetTempPath(), "close-behavior-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var settings = new SettingsManager(folder);
        var calculator = new CovenantCalculator([], [], new Dictionary<string, string>());
        var panel = new SidePanelWindow(new(), new(), new(), new(), new(), new(),
            new(new LineupStore(Path.Combine(folder, "lineups"), calculator.EquipmentRules), calculator),
            new(calculator.EquipmentRules, Path.Combine(folder, "share")), [], [], [], settings, theme, new())
        { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Width = 400, Height = 700 };
        var overlay = new Window { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        var evidence = new Dictionary<string, double>();
        var output = Path.Combine(TestOutput.Directory, "close-behavior");
        Directory.CreateDirectory(output);
        try
        {
            panel.Show(); overlay.Show(); ThemeManager.Apply(theme, night); Pump();
            using var tray = new AssistantTray(panel, overlay);
            var selector = (ComboBox)panel.FindName("CloseBehaviorSelector");
            var close = (Button)panel.FindName("CloseAssistantButton");
            selector.SelectedIndex.Should().Be(0);
            close.ToolTip.Should().Be("关闭助手（每次询问）");

            void Request(string action, bool remember, bool systemClose, string imageName)
            {
                Exception? error = null;
                var clicked = false;
                panel.Dispatcher.BeginInvoke(() =>
                {
                    var dialog = Application.Current.Windows.OfType<CloseBehaviorDialog>().Single();
                    dialog.Opacity = 0; dialog.ShowActivated = false;
                    try
                    {
                        Wait(() => dialog.IsLoaded);
                        panel.RequestUserCloseAsync().IsCompletedSuccessfully.Should().BeTrue("duplicate close must not open another modal");
                        Application.Current.Windows.OfType<CloseBehaviorDialog>().Should().ContainSingle();
                        ((CheckBox)dialog.FindName("RememberChoice")).IsChecked = remember; Pump();
                        var root = (FrameworkElement)dialog.Content;
                        foreach (var name in new[] { "TitleText", "MessageText", "ExplanationText", "RememberChoice", "SettingsHint", "TrayButton", "ExitButton" })
                        {
                            var element = (FrameworkElement)dialog.FindName(name);
                            var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
                            bounds.Right.Should().BeLessThanOrEqualTo(root.ActualWidth + 0.5, name);
                            bounds.Bottom.Should().BeLessThanOrEqualTo(root.ActualHeight + 0.5, name);
                            element.ActualWidth.Should().BeGreaterThan(0, name);
                        }
                        evidence[imageName + "-scale"] = WindowUiScale.ScaleOf(dialog);
                        var surface = ((SolidColorBrush)dialog.Background).Color;
                        foreach (var name in new[] { "MessageText", "ExplanationText", "SettingsHint" })
                        {
                            var text = (TextBlock)dialog.FindName(name);
                            var ratio = Contrast(((SolidColorBrush)text.Foreground).Color, surface);
                            evidence[imageName + "-" + name] = ratio;
                            ratio.Should().BeGreaterThanOrEqualTo(4.5);
                        }
                        var title = (TextBlock)dialog.FindName("TitleText");
                        var titleBar = (Border)((DockPanel)title.Parent).Parent;
                        evidence[imageName + "-title"] = Contrast(((SolidColorBrush)title.Foreground).Color,
                            Composite(((SolidColorBrush)titleBar.Background).Color, surface));
                        evidence[imageName + "-title"].Should().BeGreaterThanOrEqualTo(4.5);
                        var rememberBox = (CheckBox)dialog.FindName("RememberChoice");
                        evidence[imageName + "-remember"] = Contrast(((SolidColorBrush)rememberBox.Foreground).Color, surface);
                        evidence[imageName + "-remember"].Should().BeGreaterThanOrEqualTo(4.5);
                        foreach (var name in new[] { "ExitButton", "TrayButton" })
                        {
                            var button = (Button)dialog.FindName(name);
                            var footer = (Border)((StackPanel)button.Parent).Parent;
                            var ratio = Contrast(((SolidColorBrush)button.Foreground).Color,
                                Composite(((SolidColorBrush)button.Background).Color, Composite(((SolidColorBrush)footer.Background).Color, surface)));
                            evidence[imageName + "-" + name] = ratio;
                            ratio.Should().BeGreaterThanOrEqualTo(4.5);
                        }
                        Save(root, Path.Combine(output, imageName + ".png"));
                        if (action == "escape")
                            dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog), 0, Key.Escape)
                                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                        else ((Button)dialog.FindName(action)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception ex) { error = ex; dialog.Close(); }
                    finally { clicked = true; }
                }, DispatcherPriority.Loaded);
                if (systemClose)
                {
                    User32.SendMessage(new WindowInteropHelper(panel).Handle, 0x0112, (IntPtr)0xF060, IntPtr.Zero);
                    Wait(() => clicked && selector.IsEnabled);
                }
                else Await(panel.RequestUserCloseAsync());
                if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
            }

            ((Button)panel.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var settingsPanel = (Border)panel.FindName("SettingsPanel");
            Wait(() => settingsPanel.Opacity >= 0.99999 && settingsPanel.RenderTransform is TranslateTransform move && Math.Abs(move.X) < 0.01);
            ((TabControl)panel.FindName("SettingsCategoryList")).SelectedIndex = 1; Pump();
            var slider = (Slider)panel.FindName("UiScaleSlider");
            foreach (var percent in new[] { 75, 100, 150 })
            {
                slider.Value = percent; Pump();
                var imageName = $"{theme}-{night}-cancel-{percent}";
                Request("CancelButton", true, false, imageName);
                panel.IsAssistantHidden.Should().BeFalse();
                File.Exists(Path.Combine(folder, "close-behavior.json")).Should().BeFalse();
                evidence[imageName + "-scale"].Should().BeApproximately(percent / 100.0, 0.001);
            }
            slider.Value = 100; Pump();
            Request("escape", true, true, $"{theme}-{night}-escape");
            File.Exists(Path.Combine(folder, "close-behavior.json")).Should().BeFalse();
            Request("TrayButton", false, false, $"{theme}-{night}-tray");
            panel.IsAssistantHidden.Should().BeTrue();
            File.Exists(Path.Combine(folder, "close-behavior.json")).Should().BeFalse();
            tray.Restore();
            User32.IsWindowVisible(new WindowInteropHelper(panel).Handle).Should().BeTrue();
            Request("TrayButton", true, true, $"{theme}-{night}-remember");
            panel.IsAssistantHidden.Should().BeTrue();
            Await(settings.LoadMainWindowCloseBehaviorAsync()).Should().Be(MainWindowCloseBehavior.MinimizeToTray);
            tray.Restore();
            close.ToolTip.Should().Be("收起到托盘（后台识别继续）");
            Await(panel.RequestUserCloseAsync());
            panel.IsAssistantHidden.Should().BeTrue(); tray.Restore();
            selector.SelectedIndex = 2; Wait(() => selector.IsEnabled);
            close.ToolTip.Should().Be("退出程序（停止后台识别）");
            Await(settings.LoadMainWindowCloseBehaviorAsync()).Should().Be(MainWindowCloseBehavior.Exit);
            selector.SelectedIndex = 0; Wait(() => selector.IsEnabled);
            Request("CancelButton", false, false, $"{theme}-{night}-ask-again");
            var pin = (System.Windows.Controls.Primitives.ToggleButton)panel.FindName("PinTopmostButton");
            pin.IsChecked = true;
            Wait(() => ((CheckBox)panel.FindName("ForceTopmostToggle")).IsEnabled);
            var editor = new InputDialog("编辑内容保留", "名称", "尚未保存的输入")
                { Owner = panel, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            editor.Show(); Pump();
            Request("TrayButton", false, false, $"{theme}-{night}-editor-tray");
            User32.IsWindowVisible(new WindowInteropHelper(editor).Handle).Should().BeFalse();
            tray.Restore();
            User32.IsWindowVisible(new WindowInteropHelper(editor).Handle).Should().BeTrue();
            ((TextBox)editor.FindName("InputBox")).Text.Should().Be("尚未保存的输入");
            pin.IsChecked.Should().BeTrue();
            editor.Close();
            selector.SelectedIndex = 1; Wait(() => selector.IsEnabled);
            var iconField = typeof(AssistantTray).GetField("_icon", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var originalIcon = iconField.GetValue(tray)!;
            var missingIcon = iconField.GetValue(tray)!;
            missingIcon.GetType().GetField("Icon")!.SetValue(missingIcon, IntPtr.Zero);
            iconField.SetValue(tray, missingIcon);
            try
            {
                Await(panel.RequestUserCloseAsync());
                panel.IsAssistantHidden.Should().BeFalse();
                User32.IsWindowVisible(new WindowInteropHelper(panel).Handle).Should().BeTrue();
                ((TextBlock)panel.FindName("ToastText")).Text.Should().Contain("无法创建托盘图标");
                Wait(() => ((Border)panel.FindName("ToastBorder")).Opacity >= 0.99999);
                Save((FrameworkElement)VisualTreeHelper.GetParent((DependencyObject)panel.Content), Path.Combine(output, $"{theme}-{night}-tray-error.png"));
            }
            finally { iconField.SetValue(tray, originalIcon); }
            using (var locked = new FileStream(Path.Combine(folder, "close-behavior.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                selector.SelectedIndex = 2; Wait(() => selector.IsEnabled);
                close.ToolTip.Should().Be("退出程序（停止后台识别）");
                ((TextBlock)panel.FindName("ToastText")).Text.Should().Be("设置已生效，但保存失败");
                Wait(() => ((Border)panel.FindName("ToastBorder")).Opacity >= 0.99999);
                Save((FrameworkElement)VisualTreeHelper.GetParent((DependencyObject)panel.Content), Path.Combine(output, $"{theme}-{night}-preference-error.png"));
            }
            Await(settings.LoadMainWindowCloseBehaviorAsync()).Should().Be(MainWindowCloseBehavior.MinimizeToTray);
            selector.SelectedIndex = 0; Wait(() => selector.IsEnabled);
            Wait(() => ((Border)panel.FindName("ToastBorder")).Visibility == Visibility.Collapsed);
            foreach (var width in new[] { 280, 400 })
            {
                panel.Width = width; panel.Height = 600;
                ((FrameworkElement)selector.Parent).BringIntoView(); Pump();
                Save((FrameworkElement)VisualTreeHelper.GetParent((DependencyObject)panel.Content), Path.Combine(output, $"{theme}-{night}-settings-{width}.png"));
            }
            File.WriteAllText(Path.Combine(output, $"checks-{theme}-{night}.json"), JsonSerializer.Serialize(evidence));
        }
        finally { panel.Close(); overlay.Close(); Directory.Delete(folder, true); }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Wait(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(5)) { Pump(); Thread.Sleep(5); }
        condition().Should().BeTrue("the dialog interaction completes within five seconds");
    }
    private static void Await(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static T Await<T>(Task<T> task) { Await((Task)task); return task.GetAwaiter().GetResult(); }
    private static double Contrast(Color a, Color b)
    {
        static double L(Color color)
        {
            static double V(byte value) => value / 255.0 <= 0.04045 ? value / 255.0 / 12.92 : Math.Pow((value / 255.0 + 0.055) / 1.055, 2.4);
            return 0.2126 * V(color.R) + 0.7152 * V(color.G) + 0.0722 * V(color.B);
        }
        return (Math.Max(L(a), L(b)) + 0.05) / (Math.Min(L(a), L(b)) + 0.05);
    }
    private static Color Composite(Color top, Color bottom)
    {
        var alpha = top.A / 255.0;
        return Color.FromRgb((byte)(top.R * alpha + bottom.R * (1 - alpha)),
            (byte)(top.G * alpha + bottom.G * (1 - alpha)), (byte)(top.B * alpha + bottom.B * (1 - alpha)));
    }
    private static void Save(FrameworkElement element, string path)
    {
        var raw = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        raw.Render(element);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Window.GetWindow(element).Background, null, new Rect(element.RenderSize));
            drawing.DrawImage(raw, new Rect(element.RenderSize));
        }
        var bitmap = new RenderTargetBitmap(raw.PixelWidth, raw.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }
}
