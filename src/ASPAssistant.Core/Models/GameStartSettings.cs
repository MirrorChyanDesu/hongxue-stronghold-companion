namespace ASPAssistant.Core.Models;

public enum GameStartLineupPage { Available, Pending }
public enum GameStartPlannerPage { Automatic, Goals }
public enum GameStartFixedTarget { None, Lineups, Planner }

public sealed record GameStartSettings
{
    public GameStartLineupPage WithLineups { get; init; } = GameStartLineupPage.Available;
    public GameStartPlannerPage WithoutLineups { get; init; } = GameStartPlannerPage.Automatic;
    public GameStartFixedTarget FixedTarget { get; init; }
}
