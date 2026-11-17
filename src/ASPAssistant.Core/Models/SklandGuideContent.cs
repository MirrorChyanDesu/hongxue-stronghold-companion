// Added for Hongxue Stronghold Companion on 2026-11-16; see NOTICE.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

public sealed class SklandGuideContent
{
    public string GuideId { get; set; } = "";
    public string SeasonId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string SeasonName { get; set; } = "";
    public string Author { get; set; } = "";
    public string ApplicableScope { get; set; } = "";
    public List<SklandGuideStage> Stages { get; set; } = [];
    public JsonElement? OriginalGuide { get; set; }
    public JsonElement? Resources { get; set; }

    public void Validate() => SklandGuideStage.ValidateAll(Stages);
}

public sealed class SklandGuideStage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsFinal { get; set; }
    public int? StartRound { get; set; }
    public int? EndRound { get; set; }
    public List<LineupSlot> Slots { get; set; } = [];
    public List<string> RecommendedEquipments { get; set; } = [];
    public List<string> ActiveCovenants { get; set; } = [];
    public string OperationIdea { get; set; } = "";

    public static void ValidateAll(List<SklandGuideStage> stages)
    {
        if (stages is null || stages.Count == 0 || stages.Any(s => s is null) || stages.Count(s => s.IsFinal) != 1)
            throw new JsonException("攻略必须含一个成型阵容。");
        if (stages.Select(s => s.Id).Distinct().Count() != stages.Count)
            throw new JsonException("攻略阶段标识重复。");
        foreach (var stage in stages)
        {
            if (string.IsNullOrWhiteSpace(stage.Id) || string.IsNullOrWhiteSpace(stage.Name)
                || (!stage.IsFinal && (stage.StartRound.HasValue != stage.EndRound.HasValue || stage.StartRound < 1 || stage.EndRound > 15 || stage.StartRound > stage.EndRound)))
                throw new JsonException("攻略阶段名称或回合范围无效。");
            if (stage.Slots is null || stage.RecommendedEquipments is null || stage.ActiveCovenants is null
                || stage.Slots.Any(s => s is null || string.IsNullOrWhiteSpace(s.OperatorName))
                || stage.Slots.Select(s => s.OperatorName).Distinct().Count() != stage.Slots.Count)
                throw new JsonException("攻略阶段的干员或装备数据无效。");
        }
    }

    [JsonIgnore]
    public string RangeText => IsFinal ? "成型阵容" : StartRound is null ? "未指定回合"
        : StartRound == EndRound ? $"第 {StartRound} 回合" : $"第 {StartRound}–{EndRound} 回合";
}

public sealed record SklandChoice(string Id, string Name);

public sealed class SklandLoadout
{
    public string CharId { get; set; } = "";
    public string SkillId { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public List<SklandChoice> Skills { get; set; } = [];
    public List<SklandChoice> Modules { get; set; } = [];
}
