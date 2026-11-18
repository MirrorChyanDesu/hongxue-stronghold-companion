using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ASPAssistant.Core;
using ASPAssistant.Core.Services;
using Microsoft.Win32;

namespace ASPAssistant.App.Windows;

public partial class FeedbackWindow : Window
{
    private readonly FeedbackReport _report;
    public FeedbackWindow(FeedbackReport report, bool recognitionProblem)
    {
        _report = report;
        InitializeComponent();
        WindowUiScale.PrepareCentered(this);
        CategoryBox.SelectedIndex = recognitionProblem ? 1 : 0;
        ScreenshotChoice.IsEnabled = report.Recognition is { Frame.Length: > 0 };
        RecognitionText.Text = report.Recognition is { } snapshot
            ? $"已固定 {snapshot.CapturedAt.LocalDateTime:MM-dd HH:mm:ss} 的{snapshot.Source}记录，距现在 {(DateTimeOffset.Now - snapshot.CapturedAt).TotalSeconds:F0} 秒。" +
                (snapshot.Frame.Length == 0 ? "画面超过 16 MB，仅保留识别结果。" : "")
            : "暂无识别记录。请在问题出现后打开此窗口；仍可反馈程序报错或功能建议。";
    }

    private string Category => ((ComboBoxItem)CategoryBox.SelectedItem).Content.ToString()!;

    private void OnDescriptionChanged(object sender, TextChangedEventArgs e) =>
        StatusText.Text = "反馈包仅保存到本机，请检查内容后自行提交。";

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != FeedbackTabs || FeedbackTabs.SelectedIndex != 1) return;
        PreviewBox.Text = _report.Preview(Category, DescriptionBox.Text, StepsBox.Text);
    }

    private void OnScreenshotChanged(object sender, RoutedEventArgs e)
    {
        if (ScreenshotChoice.IsChecked != true)
        {
            ScreenshotPreview.Source = null;
            ScreenshotPreview.Visibility = Visibility.Collapsed;
            return;
        }
        using var stream = new MemoryStream(_report.Recognition!.Frame);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 700;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        ScreenshotPreview.Source = image;
        ScreenshotPreview.Visibility = Visibility.Visible;
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DescriptionBox.Text))
        {
            StatusText.Text = "请先填写问题描述。";
            FeedbackTabs.SelectedIndex = 0;
            DescriptionBox.Focus();
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "保存反馈包", Filter = "反馈包 (*.zip)|*.zip",
            FileName = $"鸿雪反馈-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true) return;
        ExportButton.IsEnabled = false;
        var category = Category;
        var description = DescriptionBox.Text;
        var steps = StepsBox.Text;
        var include = ScreenshotChoice.IsChecked == true;
        try
        {
            await Task.Run(() => _report.Export(dialog.FileName, category, description, steps, include));
            StatusText.Text = "反馈包已保存。可打开反馈页面，填写问题并手动附上 ZIP 文件。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warn("Feedback", $"Export failed: {ex.Message}");
            StatusText.Text = "保存失败，请选择新的文件名或可写目录。";
        }
        finally { ExportButton.IsEnabled = true; }
    }

    private void OnOpenIssue(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/bingyunhe/hongxue-stronghold-companion/issues/new") { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            AppLogger.Warn("Feedback", $"Could not open issue page: {ex.Message}");
            StatusText.Text = "无法打开浏览器，请通过项目地址进入 Issues 提交反馈。";
        }
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
