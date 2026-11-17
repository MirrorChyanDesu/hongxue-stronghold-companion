using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using ASPAssistant.Core;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

public partial class ShareLinkWindow : Window
{
    private readonly Action<string> _copyText;
    private readonly bool _isOfficialGuide;
    private bool _closed;

    public ShareLinkWindow(string lineupName, string url, Action<string> copyText)
    {
        InitializeComponent();
        WindowUiScale.PrepareCentered(this);
        _isOfficialGuide = SklandGuideImporter.IsOfficialGuideUrl(url);
        LinkBox.Text = _isOfficialGuide ? url : $"<{lineupName}> {url}";
        if (_isOfficialGuide)
        {
            ShareTitleText.Text = "阵容分享 · 森空岛原攻略";
            ShareDescriptionText.Text = "阵容与导入时一致，分享森空岛原攻略链接。";
            CopyStatusText.Text = "原攻略链接已准备，正在复制…";
        }
        _copyText = copyText;
        Closed += (_, _) => _closed = true;
        Loaded += async (_, _) => await CopyLinkAsync();
    }

    public async Task CopyLinkAsync()
    {
        if (_closed || !CopyButton.IsEnabled) return;
        CopyButton.IsEnabled = false;
        CopyButton.Content = "正在复制…";
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (_closed) return;
                try
                {
                    _copyText(LinkBox.Text);
                    CopyStatusText.Text = _isOfficialGuide ? "森空岛原攻略链接已复制，可以粘贴分享。" : "链接已复制，可以粘贴分享。";
                    return;
                }
                catch (ExternalException ex)
                {
                    if (ex.ErrorCode == unchecked((int)0x800401D0) && attempt < 2)
                    {
                        CopyStatusText.Text = "剪贴板暂时被占用，正在重试…";
                        await Task.Delay(150);
                        continue;
                    }
                    AppLogger.Warn("Lineup", $"Share link ready; clipboard copy failed: 0x{ex.ErrorCode:X8}");
                    CopyStatusText.Text = (_isOfficialGuide ? "原攻略链接已准备" : "链接已生成") + "，但暂时无法复制。可再次点击复制，或选中链接手动复制。";
                    LinkBox.Focus();
                    LinkBox.SelectAll();
                    return;
                }
            }
        }
        finally
        {
            CopyButton.IsEnabled = true;
            CopyButton.Content = "复制链接";
        }
    }

    private async void OnCopy(object sender, RoutedEventArgs e) => await CopyLinkAsync();
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
