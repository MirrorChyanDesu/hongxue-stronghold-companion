using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
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

internal static class DialogPositionViewTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        var folder = Path.Combine(Path.GetTempPath(), "dialog-position-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var calculator = new CovenantCalculator([], [], new Dictionary<string, string>());
        var appearance = new SidebarAppearance { UiScale = 1.2, Theme = theme, Night = new() { [theme] = night } };
        var side = new SidePanelWindow(new(), new(), new(), new(), new(), new(),
            new(new LineupStore(Path.Combine(folder, "lineups"), calculator.EquipmentRules), calculator),
            new(calculator.EquipmentRules, Path.Combine(folder, "share")), [], [], [], new(folder), theme,
            new SidePanelPositionSettings { FreeMovement = true }, appearance: appearance)
        { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000 };
        var output = Path.Combine(TestOutput.Directory, "dialog-position");
        Directory.CreateDirectory(output);
        var evidence = new List<object>();
        try
        {
            side.Show(); ThemeManager.Apply(theme, night); Pump();
            var ownerHwnd = new WindowInteropHelper(side).Handle;
            var work = User32.GetMonitorGeometry(0, 0).WorkArea;
            foreach (var scale in new[] { 1.2, 1.0, 1.5 })
            {
                appearance.UiScale = scale;
                var width = (int)Math.Round(400 * scale * User32.GetWindowDpiScale(ownerHwnd));
                var height = Math.Min(1023, work.Height - 40);
                foreach (var place in new[] { "middle", "left", "right", "top-short", "bottom-short" })
                {
                    var left = place == "left" ? work.Left : place == "right" ? work.Right - width : work.Left + (work.Width - width) / 2;
                    var ownerHeight = place.EndsWith("short", StringComparison.Ordinal) ? 120 : height;
                    var top = place == "top-short" ? work.Top : place == "bottom-short" ? work.Bottom - ownerHeight : work.Top + (work.Height - ownerHeight) / 2;
                    User32.SetWindowBounds(ownerHwnd, new RECT { Left = left, Top = top, Right = left + width, Bottom = top + ownerHeight }); Pump();
                    var dialog = new CloseBehaviorDialog { Owner = side, ShowActivated = false, ShowInTaskbar = false };
                    var rendered = false;
                    var stagedBeforeShow = false;
                    RECT? firstVisible = null;
                    var opacityProperty = DependencyPropertyDescriptor.FromProperty(UIElement.OpacityProperty, typeof(Window));
                    EventHandler onOpacity = (_, _) =>
                    {
                        if (dialog.Opacity > 0) firstVisible ??= User32.ReadWindowRect(new WindowInteropHelper(dialog).Handle);
                    };
                    opacityProperty.AddValueChanged(dialog, onOpacity);
                    dialog.SourceInitialized += (_, _) => stagedBeforeShow = dialog.Opacity == 0;
                    dialog.ContentRendered += (_, _) => rendered = true;
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        dialog.Show();
                        while (!rendered && watch.Elapsed < TimeSpan.FromSeconds(5)) { Pump(); Thread.Sleep(5); }
                        rendered.Should().BeTrue(); Pump();
                        stagedBeforeShow.Should().BeTrue("the native window must be transparent before it is shown");
                        dialog.Opacity.Should().Be(1, "the original opacity must be restored after layout");
                        var hwnd = new WindowInteropHelper(dialog).Handle;
                        var bounds = User32.ReadWindowRect(hwnd);
                        var owner = User32.ReadWindowRect(ownerHwnd);
                        var targetWork = User32.GetMonitorGeometry(owner.Left + owner.Width / 2, owner.Top + owner.Height / 2).WorkArea;
                        var expectedLeft = Math.Clamp(owner.Left + (owner.Width - bounds.Width) / 2, targetWork.Left, targetWork.Right - bounds.Width);
                        var expectedTop = Math.Clamp(owner.Top + (owner.Height - bounds.Height) / 2, targetWork.Top, targetWork.Bottom - bounds.Height);
                        firstVisible.Should().NotBeNull("the test must observe the first visible position");
                        firstVisible!.Value.Should().BeEquivalentTo(bounds, "the first visible frame must already be centered");
                        evidence.Add(new { Scale = scale, Place = place, Owner = owner, Dialog = bounds, FirstVisible = firstVisible, ExpectedLeft = expectedLeft, ExpectedTop = expectedTop, ShowAndLayoutMs = watch.Elapsed.TotalMilliseconds });
                        File.WriteAllText(Path.Combine(output, $"{theme}-{night}-bounds.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { IncludeFields = true }));
                        ((double)bounds.Left).Should().BeApproximately(expectedLeft, 1, $"{place}, scale={scale}: final physical width must be centered");
                        ((double)bounds.Top).Should().BeApproximately(expectedTop, 1, $"{place}, scale={scale}: final content height must be centered");
                        if (place == "middle")
                        {
                            Save(side, dialog, Path.Combine(output, $"{theme}-{night}-{scale * 100}.png"));
                            var moved = bounds;
                            moved.Left = targetWork.Left + 24; moved.Top = targetWork.Top + 30;
                            moved.Right = moved.Left + bounds.Width; moved.Bottom = moved.Top + bounds.Height;
                            User32.SetWindowBounds(hwnd, moved);
                            dialog.InvalidateVisual(); WindowUiScale.Refresh(appearance); Pump();
                            User32.ReadWindowRect(hwnd).Should().BeEquivalentTo(moved, "a later render must preserve a manually moved dialog");
                        }
                    }
                    finally { opacityProperty.RemoveValueChanged(dialog, onOpacity); dialog.Close(); }
                }
            }
        }
        finally { side.Close(); Directory.Delete(folder, true); }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Save(Window side, Window dialog, string path)
    {
        var owner = User32.ReadWindowRect(new WindowInteropHelper(side).Handle);
        var popup = User32.ReadWindowRect(new WindowInteropHelper(dialog).Handle);
        var left = Math.Min(owner.Left, popup.Left); var top = Math.Min(owner.Top, popup.Top);
        var width = Math.Max(owner.Right, popup.Right) - left; var height = Math.Max(owner.Bottom, popup.Bottom) - top;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.LightGray, null, new Rect(0, 0, width, height));
            foreach (var window in new[] { side, dialog })
            {
                var bounds = User32.ReadWindowRect(new WindowInteropHelper(window).Handle);
                var element = (FrameworkElement)VisualTreeHelper.GetParent((DependencyObject)window.Content);
                var dpi = User32.GetWindowDpiScale(new WindowInteropHelper(window).Handle);
                var image = new RenderTargetBitmap(bounds.Width, bounds.Height, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
                image.Render(element);
                drawing.DrawRectangle(window.Background, null, new Rect(bounds.Left - left, bounds.Top - top, bounds.Width, bounds.Height));
                drawing.DrawImage(image, new Rect(bounds.Left - left, bounds.Top - top, bounds.Width, bounds.Height));
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }
}
