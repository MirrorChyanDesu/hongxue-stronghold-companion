using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.Core;
using ASPAssistant.Core.Data;
using Microsoft.Win32;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow
{
    private async void OnExportConfiguration(object sender, RoutedEventArgs e)
    {
        var scope = Enum.Parse<ConfigurationScope>(((Button)sender).Tag.ToString()!);
        var dialog = new SaveFileDialog
        {
            Title = "导出" + ConfigurationArchive.ScopeName(scope), Filter = "配置包 (*.zip)|*.zip", OverwritePrompt = false,
            FileName = $"鸿雪-{ConfigurationArchive.ScopeName(scope)}-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
        };
        if (dialog.ShowDialog(this) != true) return;
        ConfigurationControls.IsEnabled = false;
        try
        {
            await Task.Run(() => ConfigurationArchive.Export(_settingsManager.DirectoryPath, dialog.FileName, scope));
            ConfigurationStatusText.Text = ConfigurationArchive.ScopeName(scope) + "已导出。配置包包含个人数据，请妥善保存。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            AppLogger.Warn("Configuration", $"Export failed: {ex.Message}");
            ConfigurationStatusText.Text = "导出失败，请选择新的文件名，并检查配置文件与目录权限。";
        }
        finally { ConfigurationControls.IsEnabled = true; }
    }

    private async void OnImportConfiguration(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "导入配置", Filter = "配置包 (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        if (!ConfirmDialog.Ask(this, "导入配置", "将按配置包范围覆盖同名文件，其他配置保留。下次启动时会先备份现有配置，再应用导入。\n\n导入准备完成后，请完全退出助手（包括托盘），再手动启动；本次导入会替换尚未应用的导入计划。", "准备导入")) return;
        ConfigurationControls.IsEnabled = false;
        try
        {
            var summary = await Task.Run(() => ConfigurationArchive.StageImport(_settingsManager.DirectoryPath, dialog.FileName));
            ConfigurationStatusText.Text = $"已准备导入{summary}。请完全退出助手后，再手动启动生效。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            AppLogger.Warn("Configuration", $"Import preparation failed: {ex.Message}");
            ConfigurationStatusText.Text = "无法导入：" + ex.Message;
        }
        finally { ConfigurationControls.IsEnabled = true; }
    }

    public void ShowConfigurationImportResult(ConfigurationImportResult result)
    {
        ConfigurationStatusText.Text = result.Message;
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            new ConfirmDialog(result.Succeeded ? "配置导入完成" : "配置导入失败", result.Message, "知道了", okOnly: true)
                { Owner = this }.ShowDialog();
        }
        Loaded += OnLoaded;
    }
}
