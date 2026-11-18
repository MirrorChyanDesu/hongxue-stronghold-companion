using System.Diagnostics;
using System.Text.Json;
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
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class ConfigurationViewTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var folder = Path.Combine(Path.GetTempPath(), "configuration-ui-" + Guid.NewGuid().ToString("N"), "含空格 中文配置路径");
        Directory.CreateDirectory(folder);
        var settings = new SettingsManager(folder);
        var calculator = new CovenantCalculator([], [], new Dictionary<string, string>());
        var panel = new SidePanelWindow(new(), new(), new(), new(), new(), new(),
            new(new LineupStore(Path.Combine(folder, "lineups"), calculator.EquipmentRules), calculator),
            new(calculator.EquipmentRules, Path.Combine(folder, "share")), [], [], [], settings, theme, new())
            { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Height = 700, Width = 280 };
        var output = Path.Combine(TestOutput.Directory, "configuration"); Directory.CreateDirectory(output);
        try
        {
            panel.Show(); ThemeManager.Apply(theme, night); Pump();
            ((Button)panel.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var settingsPanel = (Border)panel.FindName("SettingsPanel");
            Wait(() => settingsPanel.Opacity >= .9999 && settingsPanel.RenderTransform is TranslateTransform move && Math.Abs(move.X) < .01);
            settingsPanel.BeginAnimation(UIElement.OpacityProperty, null); settingsPanel.Opacity = 1;
            var transform = (TranslateTransform)settingsPanel.RenderTransform; transform.BeginAnimation(TranslateTransform.XProperty, null); transform.X = 0;
            ((TabControl)panel.FindName("SettingsCategoryList")).SelectedIndex = 3;
            var path = (TextBox)panel.FindName("ConfigurationDirectoryText");
            path.Text.Should().Be(settings.DirectoryPath); path.IsReadOnly.Should().BeTrue();
            var names = new[] { "ExportFullConfigurationButton", "ExportSoftwareConfigurationButton", "ExportLineupConfigurationButton", "ImportConfigurationButton" };
            names.Select(name => ((Button)panel.FindName(name)).Content).Should().Equal("导出完整配置", "导出软件配置", "导出阵容配置", "导入配置");
            var controls = (StackPanel)panel.FindName("ConfigurationControls");
            var card = (Border)controls.Parent;
            foreach (var width in new[] { 280, 400 })
            {
                panel.Width = width;
                foreach (var scale in new[] { 100, 150 })
                {
                    var slider = (Slider)panel.FindName("UiScaleSlider"); slider.Value = scale;
                    Wait(() => slider.IsEnabled); Pump(); card.BringIntoView(); Pump();
                    foreach (var name in names)
                    {
                        var button = (Button)panel.FindName(name);
                        var bounds = button.TransformToAncestor(controls).TransformBounds(new Rect(button.RenderSize));
                        bounds.Right.Should().BeLessThanOrEqualTo(controls.ActualWidth + .5, name);
                        var viewport = (FrameworkElement)VisualTreeHelper.GetParent((DependencyObject)panel.Content);
                        button.TransformToAncestor(viewport).TransformBounds(new Rect(button.RenderSize)).Right.Should().BeLessThanOrEqualTo(viewport.ActualWidth + .5, name);
                        button.IsEnabled.Should().BeTrue();
                    }
                    Save(panel, Path.Combine(output, $"{theme}-{night}-{width}-{scale}.png"));
                }
            }
            var dialog = new ConfirmDialog("导入配置", "将按配置包范围覆盖同名文件，其他配置保留。下次启动时会先备份现有配置，再应用导入。\n\n导入准备完成后，请完全退出助手（包括托盘），再手动启动。", "准备导入")
                { Owner = panel, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                dialog.Show(); Pump();
                WindowUiScale.ScaleOf(dialog).Should().BeApproximately(1.5, .001);
                var text = (TextBlock)dialog.FindName("MessageText");
                var ratio = Contrast(((SolidColorBrush)text.Foreground).Color, ((SolidColorBrush)dialog.Background).Color);
                ratio.Should().BeGreaterThanOrEqualTo(4.5);
                File.WriteAllText(Path.Combine(output, $"contrast-{theme}-{night}.json"), JsonSerializer.Serialize(new { Confirmation = ratio }));
                Save(dialog, Path.Combine(output, $"{theme}-{night}-confirm.png"));
            }
            finally { dialog.Close(); }
        }
        finally { panel.Close(); Directory.Delete(Directory.GetParent(folder)!.FullName, true); }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Wait(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Configuration layout did not settle"); Pump(); Thread.Sleep(5); }
    }
    private static void Save(Window window, string path)
    {
        var root = (FrameworkElement)VisualTreeHelper.GetParent((DependencyObject)window.Content);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(root.RenderSize));
        bitmap.Render(background); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    private static double Contrast(Color a, Color b)
    {
        static double C(byte n) { var x = n / 255.0; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
        static double L(Color x) => .2126 * C(x.R) + .7152 * C(x.G) + .0722 * C(x.B);
        return (Math.Max(L(a), L(b)) + .05) / (Math.Min(L(a), L(b)) + .05);
    }
}
