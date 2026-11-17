// Added for Hongxue Stronghold Companion on 2026-11-16; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Windows;

public enum StageEditorMode { Add, Rename, Rounds }
public sealed record StageEditResult(string Name, int? StartRound, int? EndRound);

public partial class LineupStageWindow : Window
{
    private readonly StageEditorMode _mode;
    private string _manualName;
    private bool _ready;
    public StageEditResult? Result { get; private set; }

    public LineupStageWindow(SklandGuideStage? stage, StageEditorMode mode)
    {
        _mode = mode;
        _manualName = stage?.Name ?? "";
        InitializeComponent();
        WindowUiScale.PrepareCentered(this);
        Title = StageTitle.Text = mode switch { StageEditorMode.Add => "新增阶段", StageEditorMode.Rename => "重命名阶段", _ => "重新选择回合" };
        var choices = new[] { "未指定" }.Concat(Enumerable.Range(1, 15).Select(n => n.ToString())).ToList();
        StageStartBox.ItemsSource = choices;
        StageEndBox.ItemsSource = choices;
        StageStartBox.SelectedIndex = stage?.StartRound ?? 0;
        StageEndBox.SelectedIndex = stage?.EndRound ?? 0;
        StageNameBox.Text = _manualName;
        if (mode == StageEditorMode.Rename)
        {
            StageRoundPanel.Visibility = Visibility.Collapsed;
            StageHint.Text = "仅修改显示名称，回合范围保持不变。";
        }
        _ready = true;
        UpdateName();
    }

    private void OnRoundsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        UpdateName();
        StageError.Text = "";
    }

    private void UpdateName()
    {
        if (_mode == StageEditorMode.Rename) return;
        if (!StageNameBox.IsReadOnly) _manualName = StageNameBox.Text;
        int start = StageStartBox.SelectedIndex, end = StageEndBox.SelectedIndex;
        StageNameBox.IsReadOnly = start != 0 || end != 0;
        StageNameBox.Text = !StageNameBox.IsReadOnly ? _manualName : start > 0 && end > 0
            ? new SklandGuideStage { StartRound = start, EndRound = end }.RangeText : "";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        int start = StageStartBox.SelectedIndex, end = StageEndBox.SelectedIndex;
        if ((start == 0) != (end == 0)) { StageError.Text = "开始和结束回合需同时填写，也可以同时留空。"; return; }
        if (start > end) { StageError.Text = "开始回合不能晚于结束回合。"; return; }
        if (string.IsNullOrWhiteSpace(StageNameBox.Text)) { StageError.Text = "请输入阶段名称，或填写完整回合范围。"; return; }
        Result = new(StageNameBox.Text.Trim(), start == 0 ? null : start, end == 0 ? null : end);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
}
