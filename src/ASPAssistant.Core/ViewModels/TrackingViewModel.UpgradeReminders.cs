using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

public partial class TrackingViewModel
{
    private readonly Dictionary<(string LineupId, int Level), UpgradeReminderProgress> _upgradeProgress = [];
    private string? _upgradeReminderLineupId;
    private UpgradeReminderDisplay? _upgradeReminder;

    public IReadOnlyList<TrackedLineup> UpgradeReminderSources { get; private set; } = [];
    public UpgradeReminderDisplay? UpgradeReminder => _upgradeReminder;
    public event Action? UpgradeReminderStateChanged;
    public event Action<UpgradeReminderDisplay>? UpgradeReminderRequested;

    public string? UpgradeReminderLineupId
    {
        get => _upgradeReminderLineupId;
        set
        {
            if (!SetProperty(ref _upgradeReminderLineupId, value)) return;
            RefreshUpgradeReminder();
            UpgradeReminderStateChanged?.Invoke();
        }
    }

    private void RefreshUpgradeReminderSources()
    {
        var sources = _trackedLineups.Where(l => l.UpgradeNotificationsEnabled && l.UpgradeReminders.Count > 0
            && TerminalUpgradeReminder.ValidationError(l.UpgradeReminders).Length == 0).ToList();
        bool sourcesChanged = !UpgradeReminderSources.SequenceEqual(sources);
        UpgradeReminderSources = sources;
        foreach (var key in _upgradeProgress.Keys.Where(k => !_trackedLineups.Any(l => l.Id == k.LineupId
                     && l.UpgradeReminders.Any(r => r.TargetLevel == k.Level))).ToList())
            _upgradeProgress.Remove(key);
        bool selectionChanged = !sources.Any(l => l.Id == _upgradeReminderLineupId);
        if (selectionChanged)
        {
            _upgradeReminderLineupId = sources.FirstOrDefault()?.Id;
        }
        RefreshUpgradeReminder();
        if (sourcesChanged) OnPropertyChanged(nameof(UpgradeReminderSources));
        if (selectionChanged) OnPropertyChanged(nameof(UpgradeReminderLineupId));
    }

    partial void OnEconomyChanged(ShopEconomy? oldValue, ShopEconomy? newValue)
    {
        if (oldValue?.Round != newValue?.Round) FollowStageRound();
        if (oldValue?.Round != newValue?.Round || oldValue?.IsPreparation != newValue?.IsPreparation)
            RefreshUpgradeReminder();
    }
    partial void OnIsTerminalLevelRestoredChanged(bool value) => RefreshUpgradeReminder();

    private void RefreshUpgradeReminder()
    {
        var source = UpgradeReminderSources.FirstOrDefault(l => l.Id == _upgradeReminderLineupId);
        UpgradeReminderDisplay? display = null;
        bool notify = false;
        if (source is not null)
        {
            bool confirmedLevel = TerminalLevel > 0 && (ManualTerminalLevel is not null || !IsTerminalLevelRestored);
            var remaining = source.UpgradeReminders.OrderBy(r => r.TargetLevel)
                .Where(r => !confirmedLevel || r.TargetLevel > TerminalLevel).ToList();
            var reminder = remaining.FirstOrDefault(r => !_upgradeProgress.TryGetValue((source.Id, r.TargetLevel), out var p) || !p.Ignored);
            if (reminder is null)
            {
                display = remaining.Count == 0
                    ? new(UpgradeReminderStatus.Completed, null, "升本计划已完成", "✓", "已达到本阵容全部升本目标")
                    : new(UpgradeReminderStatus.Ignored, null, "本局剩余提醒已忽略", "", "新一局自动恢复，也可点右侧恢复");
            }
            else
            {
                var status = !confirmedLevel || Economy?.Round is null ? UpgradeReminderStatus.Waiting
                    : Economy.Round < reminder.StartRound ? UpgradeReminderStatus.Upcoming
                    : Economy.Round < reminder.EndRound ? UpgradeReminderStatus.Due
                    : Economy.Round == reminder.EndRound ? UpgradeReminderStatus.LastRound : UpgradeReminderStatus.Overdue;
                string title = status is UpgradeReminderStatus.Waiting or UpgradeReminderStatus.Upcoming
                    ? $"下一次升至{reminder.TargetLevel}级" : $"建议升至{reminder.TargetLevel}级";
                string detail = status switch
                {
                    UpgradeReminderStatus.Waiting => "打开商店后确认回合及终端等级",
                    UpgradeReminderStatus.LastRound => "已到建议区间末回合，尚未升本",
                    UpgradeReminderStatus.Overdue => "已过建议区间，可按局势延后",
                    _ => reminder.Note.Length > 0 ? reminder.Note : "可按经济与局势调整升本时机",
                };
                display = new(status, reminder, title,
                    status == UpgradeReminderStatus.LastRound ? "区间末回合" : reminder.RoundText, detail);
                if (Economy is { IsPreparation: true } && status is UpgradeReminderStatus.Due or UpgradeReminderStatus.LastRound or UpgradeReminderStatus.Overdue)
                {
                    var key = (source.Id, reminder.TargetLevel);
                    var progress = _upgradeProgress.TryGetValue(key, out var previous) ? previous : new(source.Id, reminder.TargetLevel);
                    bool ended = status is UpgradeReminderStatus.LastRound or UpgradeReminderStatus.Overdue;
                    notify = ended ? !progress.Ended : !progress.Entered && !progress.Ended;
                    if (notify) _upgradeProgress[key] = progress with { Entered = true, Ended = ended };
                }
            }
        }
        if (_upgradeReminder != display)
        {
            _upgradeReminder = display;
            OnPropertyChanged(nameof(UpgradeReminder));
        }
        if (!notify) return;
        UpgradeReminderRequested?.Invoke(display!);
        UpgradeReminderStateChanged?.Invoke();
    }

    public void IgnoreUpgradeReminder()
    {
        if (_upgradeReminderLineupId is not { } id || UpgradeReminder?.Reminder is not { } reminder) return;
        var key = (id, reminder.TargetLevel);
        var progress = _upgradeProgress.TryGetValue(key, out var previous) ? previous : new(id, reminder.TargetLevel);
        _upgradeProgress[key] = progress with { Ignored = true };
        RefreshUpgradeReminder();
        UpgradeReminderStateChanged?.Invoke();
    }

    public void RestoreUpgradeReminders()
    {
        foreach (var key in _upgradeProgress.Keys.Where(k => k.LineupId == _upgradeReminderLineupId).ToList())
            _upgradeProgress[key] = _upgradeProgress[key] with { Ignored = false };
        RefreshUpgradeReminder();
        UpgradeReminderStateChanged?.Invoke();
    }
}
