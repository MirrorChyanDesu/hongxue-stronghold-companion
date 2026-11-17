using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class PageScaleDialogViewTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        var operators = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data")).LoadOperatorsAsync().GetAwaiter().GetResult();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var directory = Path.Combine(Path.GetTempPath(), "page-dialog-scales-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsManager(directory);
        var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
        var browse = new OperatorBrowseViewModel();
        browse.LoadOperators(operators.ToList());
        var appearance = new SidebarAppearance { Theme = theme, Night = new() { [theme] = night } };
        var side = new SidePanelWindow(browse, new EquipmentBrowseViewModel(), new TrackingViewModel(), new GameStateViewModel(), new BanViewModel(), new EnemyViewModel(),
            new LineupViewModel(new LineupStore(Path.Combine(directory, "lineups"), calculator.EquipmentRules), calculator),
            new LineupShareService(calculator.EquipmentRules, Path.Combine(directory, "share")), operators, [], [], settings, theme,
            new SidePanelPositionSettings { FreeMovement = true }, appearance: appearance)
            { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Height = 700, Left = -20000, Top = -20000 };
        var windows = new List<Window>();
        var evidence = new Dictionary<string, double>();
        side.Show();
        ThemeManager.Apply(theme, night);
        var selector = (ComboBox)side.FindName("UiScaleTargetSelector");
        var input = (TextBox)side.FindName("UiScaleValueBox");
        var slider = (Slider)side.FindName("UiScaleSlider");
        var surface = (Border)side.FindName("SettingsPanel");
        var mainTabs = (TabControl)side.FindName("MainTabs");
        void Settings(bool open)
        {
            if ((surface.Visibility == Visibility.Visible) != open)
            {
                ((Button)side.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Wait(() => open ? surface.Opacity >= 0.99999 : surface.Visibility == Visibility.Collapsed);
            }
            if (open) { ((TabControl)side.FindName("SettingsCategoryList")).SelectedIndex = 1; Pump(); }
        }
        void Enter(string text)
        {
            input.Text = text;
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Wait(() => slider.IsEnabled);
        }
        void Set(int target, int percent)
        {
            Settings(true);
            selector.SelectedIndex = target;
            Pump();
            Wait(() => slider.IsEnabled);
            Enter(percent.ToString());
        }
        void Show(Window window, string name, double expected)
        {
            window.Opacity = 0;
            window.ShowActivated = window.ShowInTaskbar = false;
            windows.Add(window);
            var rendered = false;
            window.ContentRendered += (_, _) => rendered = true;
            window.Show();
            Wait(() => rendered);
            Pump();
            WindowUiScale.ScaleOf(window).Should().BeApproximately(expected, 0.0001, name);
            var root = (Border)window.Content;
            if (expected != 1)
            {
                root.Child.Should().BeOfType<ScaledViewport>();
                ((ScaledViewport)root.Child).ContentScale.Should().BeApproximately(expected, 0.0001);
            }
            var bounds = User32.ReadWindowRect(new WindowInteropHelper(window).Handle);
            var work = User32.GetMonitorGeometry(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2).WorkArea;
            bounds.Left.Should().BeGreaterThanOrEqualTo(work.Left);
            bounds.Right.Should().BeLessThanOrEqualTo(work.Right);
            bounds.Bottom.Should().BeLessThanOrEqualTo(work.Bottom);
            if (window.WindowStartupLocation == WindowStartupLocation.CenterOwner && window.Owner is { } owner)
            {
                var ownerBounds = User32.ReadWindowRect(new WindowInteropHelper(owner).Handle);
                var ownerWork = User32.GetMonitorGeometry(ownerBounds.Left + ownerBounds.Width / 2, ownerBounds.Top + ownerBounds.Height / 2).WorkArea;
                var left = Math.Clamp(ownerBounds.Left + (ownerBounds.Width - bounds.Width) / 2, ownerWork.Left, ownerWork.Right - bounds.Width);
                var top = Math.Clamp(ownerBounds.Top + (ownerBounds.Height - bounds.Height) / 2, ownerWork.Top, ownerWork.Bottom - bounds.Height);
                ((double)bounds.Left).Should().BeApproximately(left, 1, name);
                ((double)bounds.Top).Should().BeApproximately(top, 1, name);
            }
            Save(root, $"{name}-{theme}-{night}");
        }
        try
        {
            Set(0, 125);
            ((ScrollViewer)input.Template.FindName("PART_ContentHost", input)).ActualWidth.Should().BeGreaterThan(28, "the complete percentage must remain readable");
            appearance.UiScale.Should().Be(1.25);
            side.Width.Should().BeApproximately(387.5, 1);
            Set(2, 83);
            appearance.ScaleOverrides[UiScaleTarget.Tracking].Should().Be(0.83);
            var dropdown = (Popup)selector.Template.FindName("PART_Popup", selector);
            var dropdownRoot = (FrameworkElement)dropdown.Child;
            dropdownRoot.Opacity = 0;
            selector.IsDropDownOpen = true;
            Wait(() => PresentationSource.FromVisual(dropdownRoot) is not null && Math.Abs(ScreenScale(dropdownRoot) - 1.25) < 0.02);
            evidence["dropdown-screen-scale"] = ScreenScale(dropdownRoot);
            selector.IsDropDownOpen = false;
            dropdownRoot.Opacity = 1;
            slider.Value.Should().Be(83, "integer entry is not rounded to the slider's 5% ticks");
            var width = side.Width;
            foreach (var invalid in new[] { "151", "74", "abc", "83.5" })
            {
                Enter(invalid);
                ((TextBlock)side.FindName("UiScaleInputError")).IsVisible.Should().BeTrue();
                appearance.ScaleOverrides[UiScaleTarget.Tracking].Should().Be(0.83);
                side.Width.Should().Be(width);
            }
            var error = (TextBlock)side.FindName("UiScaleInputError");
            error.BringIntoView();
            Pump();
            var card = ((SolidColorBrush)side.FindResource("CardBackgroundBrush")).Color;
            var errorBackground = Composite(((SolidColorBrush)error.Background).Color, card);
            evidence["input-error"] = Contrast(((SolidColorBrush)error.Foreground).Color, errorBackground);
            evidence["input-error"].Should().BeGreaterThanOrEqualTo(4.5);
            Save((FrameworkElement)VisualTreeHelper.GetParent((DependencyObject)side.Content), $"input-error-{theme}-{night}");
            Enter("87%");
            appearance.ScaleOverrides[UiScaleTarget.Tracking].Should().Be(0.87);
            input.Text = "83";
            input.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, input, selector) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Wait(() => slider.IsEnabled);
            appearance.ScaleOverrides[UiScaleTarget.Tracking].Should().Be(0.83);
            Set(1, 91);
            Set(3, 110);
            Set(4, 140);
            Set(5, 120);
            Settings(false);
            var contentWatch = Stopwatch.StartNew();
            for (int page = 0; page < 4; page++)
            {
                mainTabs.SelectedIndex = page;
                Pump();
                var tab = (TabItem)mainTabs.SelectedItem;
                tab.Content.Should().BeOfType<ScaledViewport>();
                var viewport = (ScaledViewport)tab.Content;
                viewport.ContentScale.Should().BeApproximately(appearance.ScaleFor((UiScaleTarget)page, appearance.UiScale) / appearance.UiScale, 0.0001);
                ((FrameworkElement)viewport.Content).Height.Should().BeGreaterThan(0).And.BeLessThan(3000);
                side.Width.Should().BeApproximately(width, 1, "switching page does not resize the outer frame");
                Save((FrameworkElement)VisualTreeHelper.GetParent((DependencyObject)side.Content), $"main-{page}-{theme}-{night}");
            }
            evidence["four-page-switch-ms-including-png"] = contentWatch.Elapsed.TotalMilliseconds;
            mainTabs.SelectedIndex = 1;
            Pump();
            var editorVm = new LineupEditorViewModel(new Lineup { Name = "缩放验证" }, operators, [], calculator);
            var editor = new LineupEditorWindow(editorVm, []) { Owner = side };
            Show(editor, "editor", 1.2);
            if (editor.Width >= 1175 && editor.Height >= 815)
            {
                var editorViewport = (ScaledViewport)((Border)editor.Content).Child;
                editorViewport.ScrollableWidth.Should().BeLessThan(2);
                editorViewport.ScrollableHeight.Should().BeLessThan(2);
            }
            var share = new ShareLinkWindow("验证", "https://example.invalid/share", _ => { }) { Owner = editor };
            Show(share, "share", 1.2);
            share.Width = 300;
            Pump();
            ((ScaledViewport)((Border)share.Content).Child).ScrollableWidth.Should().BeGreaterThan(0, "a narrow work area must leave fixed dialog content reachable");
            share.Close();
            var confirm = new ConfirmDialog("确认操作", "正文、说明和按钮应跟随阵容编辑比例", danger: true) { Owner = editor };
            Show(confirm, "confirm", 1.2);
            var confirmBackground = ((SolidColorBrush)confirm.Background).Color;
            var message = (TextBlock)confirm.FindName("MessageText");
            evidence["confirm-body"] = Contrast(((SolidColorBrush)message.Foreground).Color, confirmBackground);
            evidence["confirm-body"].Should().BeGreaterThanOrEqualTo(4.5);
            var title = (TextBlock)confirm.FindName("TitleText");
            var titleBar = (Border)((DockPanel)title.Parent).Parent;
            evidence["confirm-title"] = Contrast(((SolidColorBrush)title.Foreground).Color,
                Composite(((SolidColorBrush)titleBar.Background).Color, confirmBackground));
            evidence["confirm-title"].Should().BeGreaterThanOrEqualTo(4.5);
            foreach (var name in new[] { "CancelButton", "OkButton" })
            {
                var button = (Button)confirm.FindName(name);
                var footer = (Border)((StackPanel)button.Parent).Parent;
                var background = Composite(((SolidColorBrush)button.Background).Color,
                    Composite(((SolidColorBrush)footer.Background).Color, confirmBackground));
                evidence["confirm-" + name] = Contrast(((SolidColorBrush)button.Foreground).Color, background);
                evidence["confirm-" + name].Should().BeGreaterThanOrEqualTo(4.5);
            }
            confirm.Close();
            var prompt = new InputDialog("输入", "请输入名称") { Owner = editor };
            Show(prompt, "input", 1.2);
            prompt.Close();
            var equipment = new EquipmentPickerWindow([], calculator.EquipmentRules, "角峰", [], []) { Owner = editor };
            Show(equipment, "equipment", 1.2);
            equipment.Close();
            var strategy = new StrategyPickerWindow([], []) { Owner = editor };
            Show(strategy, "strategy", 1.2);
            strategy.Close();
            var targets = new TargetPickerWindow(editorVm.AllCovenants, editorVm.ComputeLate(), editorVm.TargetCovenants, editorVm.HasSecondTier) { Owner = editor };
            Show(targets, "targets", 1.2);
            targets.Close();
            var simple = new SimplePickerDialog("选择", [new("one", "一", null)], null, "说明") { Owner = editor };
            Show(simple, "simple-picker", 1.2);
            simple.Close();
            var update = new UpdateDialog(new UpdateInfo("v9.9.9", "9.9.9", "更新说明", "https://example.invalid/package.zip", "sample.zip", null)) { Owner = editor };
            Show(update, "update", 1.2);
            update.Close();
            var picker = new OperatorPickerWindow(operators, "", new HashSet<string>()) { Owner = editor };
            Show(picker, "operator-from-editor", 1.2);
            var list = (ListBox)picker.FindName("OperatorsList");
            list.Items.Count.Should().BeGreaterThan(100);
            var realized = Enumerable.Range(0, list.Items.Count).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) is not null);
            realized.Should().BeGreaterThan(0).And.BeLessThan(list.Items.Count, "scaled outer scrolling must preserve virtualization");
            evidence["realized-operator-items"] = realized;
            evidence["total-operator-items"] = list.Items.Count;
            picker.Close();
            var trackingPicker = new OperatorPickerWindow(operators, "", new HashSet<string>()) { Owner = side };
            Show(trackingPicker, "operator-from-tracking", 0.83);
            trackingPicker.Close();
            var custom = new CustomOperatorSelectionWindow(operators.Where(op => op.IsCustom)) { Owner = side };
            Show(custom, "custom-operators", 0.91);
            custom.Close();
            editor.WindowState = WindowState.Minimized;
            Pump();
            Set(5, 130);
            editor.WindowState = WindowState.Normal;
            Wait(() => ((ScaledViewport)((Border)editor.Content).Child).ContentScale == 1.3);
            Set(6, 105);
            Settings(false);
            var overridden = new OperatorPickerWindow(operators, "", new HashSet<string>()) { Owner = editor };
            Show(overridden, "operator-override", 1.05);
            overridden.Close();
            var overriddenTracking = new OperatorPickerWindow(operators, "", new HashSet<string>()) { Owner = side };
            Show(overriddenTracking, "operator-override-tracking", 1.05);
            overriddenTracking.Close();
            var overriddenCustom = new CustomOperatorSelectionWindow(operators.Where(op => op.IsCustom)) { Owner = side };
            Show(overriddenCustom, "custom-override", 1.05);
            overriddenCustom.Close();
            var tooltip = new ToolTip { PlacementTarget = side.TrackingView, Content = new TextBlock { Text = "跟随追踪比例" }, Opacity = 0 };
            tooltip.IsOpen = true;
            Wait(() => PresentationSource.FromVisual(tooltip) is not null && Math.Abs(ScreenScale(tooltip) - 0.83) < 0.02);
            evidence["tooltip-screen-scale"] = ScreenScale(tooltip);
            tooltip.IsOpen = false;
            var menu = new ContextMenu { PlacementTarget = side.TrackingView, Opacity = 0 };
            var more = new MenuItem { Header = "更多" };
            more.Items.Add(new MenuItem { Header = "子菜单项" });
            menu.Items.Add(more);
            menu.IsOpen = true;
            Wait(() => PresentationSource.FromVisual(menu) is not null && Math.Abs(ScreenScale(menu) - 0.83) < 0.02);
            evidence["menu-screen-scale"] = ScreenScale(menu);
            more.ApplyTemplate();
            var submenu = (Popup)more.Template.FindName("PART_Popup", more);
            var submenuRoot = (FrameworkElement)submenu.Child;
            submenuRoot.Opacity = 0;
            more.IsSubmenuOpen = true;
            Wait(() => PresentationSource.FromVisual(submenuRoot) is not null && Math.Abs(ScreenScale(submenuRoot) - 0.83) < 0.02);
            evidence["submenu-screen-scale"] = ScreenScale(submenuRoot);
            more.IsSubmenuOpen = false;
            menu.IsOpen = false;
            Settings(true);
            selector.SelectedIndex = 2;
            Pump();
            ((Button)side.FindName("ResetUiScaleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(() => slider.IsEnabled);
            appearance.ScaleOverrides.Should().NotContainKey(UiScaleTarget.Tracking);
            ((CheckBox)side.FindName("UiScaleFollowToggle")).IsChecked.Should().BeTrue();
            ((TabItem)mainTabs.Items[1]).Content.Should().NotBeOfType<ScaledViewport>();
            var loaded = Await(settings.LoadSidebarAppearanceAsync());
            loaded.UiScale.Should().Be(1.25);
            loaded.ScaleOverrides[UiScaleTarget.OperatorPicker].Should().Be(1.05);
            loaded.ScaleOverrides.Should().NotContainKey(UiScaleTarget.Tracking);
            var output = Path.Combine(TestOutput.Directory, "page-dialog-scales");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, $"checks-{theme}-{night}.json"), JsonSerializer.Serialize(evidence));
            File.WriteAllText(Path.Combine(output, $"contrast-input-{theme}-{night}.json"), JsonSerializer.Serialize(new { ErrorText = evidence["input-error"] }));
        }
        finally
        {
            foreach (var window in windows.AsEnumerable().Reverse()) window.Close();
            side.Close();
        }
    }

    private static double ScreenScale(FrameworkElement element)
        => (element.PointToScreen(new Point(element.ActualWidth, 0)).X - element.PointToScreen(new Point()).X)
            / element.ActualWidth / VisualTreeHelper.GetDpi(element).DpiScaleX;
    private static Color Composite(Color top, Color bottom)
    {
        var alpha = top.A / 255.0;
        return Color.FromRgb((byte)(top.R * alpha + bottom.R * (1 - alpha)), (byte)(top.G * alpha + bottom.G * (1 - alpha)), (byte)(top.B * alpha + bottom.B * (1 - alpha)));
    }
    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double V(byte value) => value / 255.0 <= 0.04045 ? value / 255.0 / 12.92 : Math.Pow((value / 255.0 + 0.055) / 1.055, 2.4);
            return 0.2126 * V(c.R) + 0.7152 * V(c.G) + 0.0722 * V(c.B);
        }
        return (Math.Max(L(a), L(b)) + 0.05) / (Math.Min(L(a), L(b)) + 0.05);
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Wait(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(5)) { Pump(); Thread.Sleep(5); }
        condition().Should().BeTrue("the rendered UI reaches its expected state within five seconds");
    }
    private static T Await<T>(Task<T> task) { Wait(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void Save(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var raw = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        raw.Render(element);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Window.GetWindow(element).Background, null, new Rect(element.RenderSize));
            drawing.DrawImage(raw, new Rect(element.RenderSize));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var output = Path.Combine(TestOutput.Directory, "page-dialog-scales");
        Directory.CreateDirectory(output);
        using var file = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(file);
    }
}
