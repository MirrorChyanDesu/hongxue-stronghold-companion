using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow
{
    private GameStartSettings _gameStartSettings = new();
    private bool _syncingGameStart = true;

    public void InitGameStartSettings(GameStartSettings settings)
    {
        _gameStartSettings = settings;
        SyncGameStartControls();
    }

    private void SyncGameStartControls()
    {
        _syncingGameStart = true;
        GameStartWithLineups.SelectedIndex = (int)_gameStartSettings.WithLineups;
        GameStartWithoutLineups.SelectedIndex = (int)_gameStartSettings.WithoutLineups;
        GameStartFixLineups.IsChecked = _gameStartSettings.FixedTarget == GameStartFixedTarget.Lineups;
        GameStartFixPlanner.IsChecked = _gameStartSettings.FixedTarget == GameStartFixedTarget.Planner;
        var lineup = _gameStartSettings.WithLineups == GameStartLineupPage.Available ? "本局可用" : "待确认";
        var planner = _gameStartSettings.WithoutLineups == GameStartPlannerPage.Automatic ? "自动推荐" : "目标";
        GameStartEffectiveTitle.Text = _gameStartSettings.FixedTarget switch
        {
            GameStartFixedTarget.Lineups => $"已固定到：我的阵容 · {lineup}",
            GameStartFixedTarget.Planner => $"已固定到：规划 · {planner}",
            _ => "按阵容情况跳转"
        };
        GameStartEffectiveHint.Text = _gameStartSettings.FixedTarget == GameStartFixedTarget.None
            ? "本局可用或待确认至少有一套时进入我的阵容，否则进入规划。"
            : "无论是否有可用阵容，识别成功后均进入此页。";
        _syncingGameStart = false;
    }

    private async void OnGameStartPageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingGameStart) return;
        _gameStartSettings = _gameStartSettings with
        {
            WithLineups = (GameStartLineupPage)GameStartWithLineups.SelectedIndex,
            WithoutLineups = (GameStartPlannerPage)GameStartWithoutLineups.SelectedIndex
        };
        SyncGameStartControls();
        await SavePreferenceAsync(() => _settingsManager.SaveGameStartSettingsAsync(_gameStartSettings), GameStartCard);
    }

    private async void OnGameStartFixedClick(object sender, RoutedEventArgs e)
    {
        var button = (ToggleButton)sender;
        _gameStartSettings = _gameStartSettings with { FixedTarget = button.IsChecked == true
            ? ReferenceEquals(button, GameStartFixLineups) ? GameStartFixedTarget.Lineups : GameStartFixedTarget.Planner
            : GameStartFixedTarget.None };
        SyncGameStartControls();
        await SavePreferenceAsync(() => _settingsManager.SaveGameStartSettingsAsync(_gameStartSettings), GameStartCard);
    }

    public void NavigateAfterGameStart()
    {
        UpdateLineupRunInfo();
        bool lineups = _gameStartSettings.FixedTarget == GameStartFixedTarget.Lineups
            || (_gameStartSettings.FixedTarget == GameStartFixedTarget.None
                && LineupVm.Recommendations.Any(r => r.Kind is RecommendationKind.Usable or RecommendationKind.BannedButFine or RecommendationKind.NeedsRepair));
        MainTabs.SelectedItem = LineupsTab;
        LineupsSegment.SelectedIndex = lineups ? 0 : 1;
        if (lineups)
        {
            LineupView.ShowRecommendations(_gameStartSettings.WithLineups == GameStartLineupPage.Available
                ? LineupAvailabilityFilter.Available : LineupAvailabilityFilter.Pending);
            return;
        }
        if (_gameStartSettings.WithoutLineups == GameStartPlannerPage.Automatic) PlannerVm.ShowAutoPageCommand.Execute(null);
        else PlannerVm.ShowGoalPageCommand.Execute(null);
    }
}
