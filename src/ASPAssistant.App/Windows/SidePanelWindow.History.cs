using System.IO;
using System.Text.Json;
using System.Windows;
using ASPAssistant.Core;
using ASPAssistant.Core.Data;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow
{
    private void OnOpenUpdateHistory(object sender, RoutedEventArgs e)
    {
        try
        {
            var entries = UpdateHistory.Load(Path.Combine(AppContext.BaseDirectory, "data", "update-history.json"));
            new UpdateHistoryWindow(entries) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            AppLogger.Warn("UpdateHistory", $"Cannot read bundled history: {ex.Message}");
            ShowToast("无法读取更新日志，请检查程序数据文件是否完整。");
        }
    }
}
