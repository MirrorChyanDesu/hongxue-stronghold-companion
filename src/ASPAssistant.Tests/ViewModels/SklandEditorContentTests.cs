using System.Net;
using System.Net.Http;
using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class SklandEditorContentTests
{
    private static async Task<(Lineup Source, LineupEditorViewModel Editor)> Create()
    {
        var data = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var operators = await data.LoadOperatorsAsync();
        var strategies = await data.LoadStrategiesAsync();
        var (equipment, _) = await data.LoadEquipmentAsync();
        var (covenants, map) = await data.LoadCovenantsAsync();
        using var http = new HttpClient(new Handler());
        var source = (await new SklandGuideImporter(http, operators, strategies).ImportAsync(Services.SklandGuideImporterTests.Url)).Lineup;
        return (source, new LineupEditorViewModel(source, operators, equipment, new CovenantCalculator(operators, covenants, map)));
    }

    [Fact]
    public async Task FullGuideSurvivesEditingStoreShareAndReopenWithoutMutatingTheSource()
    {
        var (source, editor) = await Create();
        var original = JsonSerializer.Serialize(source);
        editor.SelectPhase(editor.PhaseLabels.Keys.ElementAt(1));
        editor.CurrentGuideStage!.Name = "自定义前期";
        editor.CurrentGuideStage.StartRound = 2;
        editor.CurrentGuideStage.EndRound = 6;
        editor.CurrentGuideStage.OperationIdea += "\n本地补充讲解";
        var gear = editor.CurrentGuideStage.RecommendedEquipments[0];
        editor.ToggleSlotEquipment(0, gear);
        editor.SetGuideLoadout(0, "", "");
        var saved = editor.ToLineup();
        JsonSerializer.Serialize(source).Should().Be(original);
        saved.Slots.Should().HaveCount(8);
        saved.EarlySlots.Should().BeEmpty(); saved.MidSlots.Should().BeEmpty(); saved.SupportSlots.Should().BeEmpty();
        saved.SklandGuide!.Stages[1].Slots.Should().HaveCount(14);
        saved.SklandGuide.Stages[1].Slots[0].Equipments.Should().Contain(gear);
        saved.SklandGuide.Stages[1].Slots[0].GuideLoadout!.SkillId.Should().BeEmpty();
        var folder = Path.Combine(Path.GetTempPath(), "skland-editor-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new LineupStore(folder, EquipmentTestData.Rules);
            await store.SaveAsync(saved);
            var reloaded = (await store.LoadAllAsync()).Single();
            var share = new LineupShareService(EquipmentTestData.Rules, folder);
            var shared = await share.ImportAsync(JsonSerializer.Serialize(reloaded));
            shared.SklandGuide.Should().BeEquivalentTo(saved.SklandGuide, options => options
                .Excluding(g => g.OriginalGuide).Excluding(g => g.Resources));
            System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(shared.SklandGuide!.OriginalGuide!.Value.GetRawText()),
                System.Text.Json.Nodes.JsonNode.Parse(saved.SklandGuide!.OriginalGuide!.Value.GetRawText())).Should().BeTrue();
            System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(shared.SklandGuide.Resources!.Value.GetRawText()),
                System.Text.Json.Nodes.JsonNode.Parse(saved.SklandGuide.Resources!.Value.GetRawText())).Should().BeTrue();
            var reopened = new LineupEditorViewModel(shared, editor.AllOperators, editor.AllEquipment,
                new CovenantCalculator(editor.AllOperators, editor.AllCovenants, new Dictionary<string, string>()));
            reopened.SelectPhase(reopened.PhaseLabels.Keys.ElementAt(1));
            reopened.CurrentGuideStage!.Name.Should().Be("自定义前期");
            reopened.CurrentGuideStage.OperationIdea.Should().EndWith("本地补充讲解");
            reopened.Slots[0].GuideLoadout!.SkillId.Should().BeEmpty();
            reopened.Slots[0].Equipments.Should().Contain(gear);
            shared.SklandGuide!.OriginalGuide!.Value.GetProperty("guide").GetProperty("tabs").GetArrayLength().Should().Be(3);
            shared.SklandGuide.Resources!.Value.GetProperty("chars").EnumerateObject().Should().HaveCount(23);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task ResetRestoresAllGuideEditsAndSelections()
    {
        var (_, editor) = await Create();
        var initial = JsonSerializer.Serialize(editor.ToLineup().SklandGuide);
        editor.SelectPhase(editor.PhaseLabels.Keys.ElementAt(1));
        editor.CurrentGuideStage!.Name = "修改";
        editor.CurrentGuideStage.OperationIdea = "修改";
        editor.CurrentGuideStage.RecommendedEquipments.Clear();
        editor.SetGuideLoadout(0, "", "");
        editor.Reset();
        JsonSerializer.Serialize(editor.ToLineup().SklandGuide).Should().Be(initial);
    }

    [Fact]
    public async Task StageDuplicateOperatorsKeepTheirOwnLoadoutAndEliteState()
    {
        var (_, editor) = await Create();
        var saved = editor.ToLineup();
        var stages = saved.SklandGuide!.Stages;
        stages[0].Slots.Single(s => s.OperatorName == "蓝毒").IsElite.Should().BeTrue();
        stages[1].Slots.Single(s => s.OperatorName == "蓝毒").IsElite.Should().BeFalse();
        var originalId = stages[0].Slots.Single(s => s.OperatorName == "蓝毒").GuideLoadout!.SkillId;
        stages[1].Slots.Single(s => s.OperatorName == "蓝毒").GuideLoadout!.SkillId = "";
        stages[0].Slots.Single(s => s.OperatorName == "蓝毒").GuideLoadout!.SkillId.Should().Be(originalId);
    }

    [Fact]
    public async Task ShareFingerprintChangesForNotesLoadoutAndMembers()
    {
        var (_, editor) = await Create();
        var saved = editor.ToLineup();
        var first = LineupShareService.ContentFingerprint(saved);
        saved.SklandGuide!.Stages[1].OperationIdea += "补充";
        LineupShareService.ContentFingerprint(saved).Should().NotBe(first);
        var second = LineupShareService.ContentFingerprint(saved);
        saved.SklandGuide.Stages[1].Slots[0].GuideLoadout!.ModuleId = "";
        LineupShareService.ContentFingerprint(saved).Should().NotBe(second);
        var third = LineupShareService.ContentFingerprint(saved);
        saved.SklandGuide.Stages[1].Slots[0].IsElite = !saved.SklandGuide.Stages[1].Slots[0].IsElite;
        LineupShareService.ContentFingerprint(saved).Should().NotBe(third);
    }

    [Fact]
    public async Task EmptyStageNamesAndReversedRoundsCannotBeSavedAndInvalidSharesAreRejected()
    {
        var (_, editor) = await Create();
        editor.SelectPhase(editor.PhaseLabels.Keys.ElementAt(1));
        editor.CurrentGuideStage!.Name = "";
        editor.ValidationError.Should().Contain("阶段名称");
        editor.CurrentGuideStage.Name = "阶段";
        editor.CurrentGuideStage.StartRound = 8;
        editor.CurrentGuideStage.EndRound = 2;
        editor.ValidationError.Should().Contain("开始回合");
        var share = new LineupShareService(EquipmentTestData.Rules, Path.GetTempPath());
        await Assert.ThrowsAsync<JsonException>(() => share.ImportAsync(JsonSerializer.Serialize(editor.ToLineup())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddedStagesAndNamesPersistForManualAndImportedLineupsAndResetRemovesUnsavedStages(bool imported)
    {
        var (guide, originalEditor) = await Create();
        var source = imported ? guide : new Lineup { Name = "手工阵容", Slots = guide.Slots,
            EarlySlots = [new() { OperatorName = "蓝毒" }] };
        var before = JsonSerializer.Serialize(source);
        var editor = new LineupEditorViewModel(source, originalEditor.AllOperators, originalEditor.AllEquipment,
            new CovenantCalculator(originalEditor.AllOperators, originalEditor.AllCovenants, new Dictionary<string, string>()));
        int count = editor.PhaseLabels.Count;
        editor.AddStage("第 14–15 回合", 14, 15);
        var id = editor.CurrentStage.Id;
        editor.SetSlotOperator(0, "蓝毒");
        editor.SetStageNotes("新增阶段讲解");
        editor.UpdateStage(editor.CurrentPhase, "过渡准备", 14, 15);
        editor.CurrentStage.Id.Should().Be(id);
        editor.CurrentStage.StartRound.Should().Be(14);
        editor.AddStage("临时补位", null, null);
        editor.ValidationError.Should().BeEmpty();
        var saved = editor.ToLineup();
        saved.Slots.Select(s => s.OperatorName).Should().Equal(source.Slots.Select(s => s.OperatorName));
        saved.EarlySlots.Should().BeEquivalentTo(source.EarlySlots);
        JsonSerializer.Serialize(source).Should().Be(before);
        var share = new LineupShareService(EquipmentTestData.Rules, Path.GetTempPath());
        var restored = await share.ImportAsync(JsonSerializer.Serialize(saved));
        var reopened = new LineupEditorViewModel(restored, editor.AllOperators, editor.AllEquipment,
            new CovenantCalculator(editor.AllOperators, editor.AllCovenants, new Dictionary<string, string>()));
        reopened.PhaseLabels.Count.Should().Be(count + 2);
        reopened.SelectPhase(reopened.PhaseLabels.Single(p => p.Value == "过渡准备").Key);
        reopened.CurrentStage.Id.Should().Be(id);
        reopened.CurrentStage.OperationIdea.Should().Be("新增阶段讲解");
        reopened.Slots[0].OperatorName.Should().Be("蓝毒");
        reopened.SelectPhase(reopened.PhaseLabels.Single(p => p.Value == "临时补位").Key);
        reopened.CurrentStage.StartRound.Should().BeNull();
        editor.Reset();
        editor.PhaseLabels.Should().HaveCount(count);
        (editor.ToLineup().SklandGuide?.Stages ?? editor.ToLineup().EditorStages)?.Should().NotContain(s => s.Id == id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicatedStageIsIndependentAndPersistsAfterItsSourceWithoutChangingTheFinalLineup(bool imported)
    {
        var (guide, original) = await Create();
        var source = imported ? guide : new Lineup { Name = "手工阵容", Slots = guide.Slots,
            EarlySlots = guide.SklandGuide!.Stages[1].Slots.Take(2).ToList() };
        var sourceJson = JsonSerializer.Serialize(source);
        LineupEditorViewModel Open(Lineup lineup) => new(lineup, original.AllOperators, original.AllEquipment,
            new CovenantCalculator(original.AllOperators, original.AllCovenants, new Dictionary<string, string>()));
        var editor = Open(source);
        var phase = imported ? editor.PhaseLabels.Keys.ElementAt(1) : LineupPhase.前期;
        editor.SelectPhase(phase);
        editor.UpdateStage(phase, "第 2–5 回合", 2, 5);
        editor.SetStageNotes("原页讲解");
        editor.CurrentStage.RecommendedEquipments.Add("人事部文档");
        editor.CurrentStage.ActiveCovenants.Add("乌萨斯");
        var initial = editor.ToLineup();
        var definitions = initial.SklandGuide?.Stages ?? initial.EditorStages!;
        var index = definitions.FindIndex(s => s.Id == editor.CurrentStage.Id);
        var stageJson = JsonSerializer.Serialize(definitions[index]);
        editor.DuplicateStage(phase);
        var copyId = editor.CurrentStage.Id;
        editor.PhaseLabels.Keys.ElementAt(index + 1).Should().Be(editor.CurrentPhase);
        editor.CurrentStage.Name.Should().Be("第 2–5 回合 副本");
        editor.CurrentStage.IsFinal.Should().BeFalse();
        editor.CurrentStage.StartRound.Should().Be(2); editor.CurrentStage.EndRound.Should().Be(5);
        editor.Slots[0].IsReserve.Should().BeFalse();
        editor.CurrentStage.RecommendedEquipments.Should().Equal(definitions[index].RecommendedEquipments);
        editor.CurrentStage.ActiveCovenants.Should().Equal(definitions[index].ActiveCovenants);
        editor.SetStageNotes("副本讲解");
        editor.CurrentStage.RecommendedEquipments.Clear(); editor.CurrentStage.ActiveCovenants.Clear();
        editor.Slots[0].Equipments.Clear(); editor.Slots[0].CandidateEquipments.Clear();
        editor.Slots[0].Tags.Add(LineupTag.经济);
        editor.Slots[0].GuideLoadout!.Skills.Clear(); editor.Slots[0].GuideLoadout!.SkillId = "";
        var saved = editor.ToLineup();
        var stages = saved.SklandGuide?.Stages ?? saved.EditorStages!;
        JsonSerializer.Serialize(stages[index]).Should().Be(stageJson);
        stages[index + 1].Id.Should().Be(copyId);
        stages.Count(s => s.IsFinal).Should().Be(1);
        JsonSerializer.Serialize(saved.Slots).Should().Be(JsonSerializer.Serialize(initial.Slots));
        var share = new LineupShareService(EquipmentTestData.Rules, Path.GetTempPath());
        var reopened = Open(await share.ImportAsync(JsonSerializer.Serialize(saved)));
        reopened.SelectPhase(reopened.PhaseLabels.Keys.ElementAt(index + 1));
        reopened.CurrentStage.Id.Should().Be(copyId);
        reopened.CurrentStage.OperationIdea.Should().Be("副本讲解");
        reopened.Slots[0].GuideLoadout!.Skills.Should().BeEmpty();
        editor.Reset();
        editor.PhaseLabels.Should().HaveCount(definitions.Count);
        (editor.ToLineup().SklandGuide?.Stages ?? editor.ToLineup().EditorStages)?.Should().NotContain(s => s.Id == copyId);
        JsonSerializer.Serialize(source).Should().Be(sourceJson);
    }

    [Fact]
    public async Task OldFinalDisplayNameChangesWithoutChangingTheFinalIdentity()
    {
        var (source, originalEditor) = await Create();
        source.SklandGuide!.Stages.Single(s => s.IsFinal).Name = "大成";
        var editor = new LineupEditorViewModel(source, originalEditor.AllOperators, originalEditor.AllEquipment,
            new CovenantCalculator(originalEditor.AllOperators, originalEditor.AllCovenants, new Dictionary<string, string>()));
        editor.PhaseLabels[LineupPhase.后期].Should().Be("成型阵容");
        editor.ToLineup().SklandGuide!.Stages.Single(s => s.IsFinal).Id.Should().Be("final");
        source.SklandGuide.Stages.Single(s => s.IsFinal).Name.Should().Be("大成");
    }

    [Fact]
    public async Task LegacyReservesBecomeOrdinarySlotsWithoutOverridingCanonicalRepairChanges()
    {
        var (guide, original) = await Create();
        var source = new Lineup { Slots = guide.Slots.Take(3).ToList() };
        LineupEditorViewModel Open(Lineup lineup) => new(lineup, original.AllOperators, original.AllEquipment,
            new CovenantCalculator(original.AllOperators, original.AllCovenants, new Dictionary<string, string>()));
        source.EditorStages = [new() { Id = "final", Name = "成型阵容", IsFinal = true,
            Slots = [new() { OperatorName = "棘刺", IsReserve = true }] }];
        var editor = Open(source);
        editor.Slots.Should().HaveCount(15);
        editor.FilledSlotCount.Should().Be(4);
        editor.Slots.Should().OnlyContain(s => !s.IsReserve);
        var saved = editor.ToLineup();
        saved.Slots.Should().HaveCount(4);
        saved.EditorStages!.Single(s => s.IsFinal).Slots.Should().HaveCount(4);
        var reopened = Open(JsonSerializer.Deserialize<Lineup>(JsonSerializer.Serialize(saved))!);
        reopened.FilledSlotCount.Should().Be(4);
        saved.Slots = [new() { OperatorName = "夜莺" }];
        var repaired = Open(saved);
        repaired.ToLineup().Slots.Should().ContainSingle().Which.OperatorName.Should().Be("夜莺");
        source.EditorStages[0].Slots[0].IsReserve.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 3, 4, 7, false)]
    [InlineData(1, 4, 4, 7, true)]
    [InlineData(1, 7, 2, 3, true)]
    [InlineData(6, 9, 1, 5, false)]
    [InlineData(6, 9, 3, 6, true)]
    public async Task StageRoundConflictsAreInclusiveAndIndependentOfDisplayOrder(int firstStart, int firstEnd, int secondStart, int secondEnd, bool conflict)
    {
        var (_, editor) = await Create();
        foreach (var phase in editor.PhaseLabels.Keys.Where(p => p != LineupPhase.后期))
            editor.UpdateStage(phase, editor.PhaseLabels[phase], null, null);
        editor.AddStage("阶段甲", firstStart, firstEnd);
        editor.AddStage("阶段乙", secondStart, secondEnd);
        if (conflict) editor.ValidationError.Should().Contain("阶段甲").And.Contain("阶段乙").And.Contain("冲突");
        else editor.ValidationError.Should().BeEmpty();
        editor.UpdateStage(editor.CurrentPhase, "未指定回合", null, null);
        editor.UpdateStage(LineupPhase.后期, "成型阵容", firstStart, firstEnd);
        editor.ValidationError.Should().BeEmpty("final and unbounded stages do not compete for round ranges");
    }

    [Fact]
    public async Task EveryPageHasFifteenSlotsAndLegacyGuideMembersAreRestoredWithoutChangingTheOriginal()
    {
        var (source, original) = await Create();
        foreach (var stage in source.SklandGuide!.Stages)
            foreach (var member in stage.Slots) member.IsReserve = true;
        var json = JsonSerializer.Serialize(source);
        var editor = new LineupEditorViewModel(source, original.AllOperators, original.AllEquipment,
            new CovenantCalculator(original.AllOperators, original.AllCovenants, new Dictionary<string, string>()));
        foreach (var phase in editor.PhaseLabels.Keys)
        {
            editor.SelectPhase(phase);
            editor.Slots.Should().HaveCount(15);
            editor.Slots.Should().OnlyContain(s => !s.IsReserve);
            editor.FilledSlotCount.Should().Be(editor.CurrentStage.Slots.Count);
        }
        var saved = editor.ToLineup();
        saved.Slots.Should().HaveCount(8);
        var tracking = new TrackingViewModel(); tracking.TrackLineup(saved);
        tracking.StageChoices.Should().HaveCount(3);
        foreach (var choice in tracking.StageChoices)
        {
            tracking.SelectStage(choice);
            tracking.ActiveEntries.Where(e => e.Type == TrackingType.Operator).Select(e => e.Name).Should()
                .BeEquivalentTo(saved.SklandGuide!.Stages.Single(s => s.Id == choice.StageId).Slots.Select(s => s.OperatorName));
        }
        JsonSerializer.Serialize(source).Should().Be(json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StageReorderingKeepsFinalFirstAndPreservesIdentityContentsRoundTripResetAndAutoTracking(bool imported)
    {
        var (guide, original) = await Create();
        var source = imported ? guide : new Lineup { Slots = guide.Slots,
            EarlySlots = [new() { OperatorName = "蓝毒", Equipments = ["人事部文档"] }] };
        var json = JsonSerializer.Serialize(source);
        LineupEditorViewModel Open(Lineup lineup) => new(lineup, original.AllOperators, original.AllEquipment,
            new CovenantCalculator(original.AllOperators, original.AllCovenants, new Dictionary<string, string>()));
        var editor = Open(source);
        editor.PhaseLabels.Keys.First().Should().Be(LineupPhase.后期);
        var initialOrder = editor.PhaseLabels.Keys.ToArray();
        editor.AddStage("末段", 14, 15); editor.SetSlotOperator(0, "蓝毒"); editor.SetStageNotes("末段独立讲解");
        var last = editor.CurrentPhase;
        var first = editor.PhaseLabels.Keys.ElementAt(1);
        editor.SelectPhase(first);
        var currentId = editor.CurrentStage.Id; var slots = editor.Slots;
        var before = editor.ToLineup();
        var contents = (before.SklandGuide?.Stages ?? before.EditorStages!).ToDictionary(s => s.Id, s => JsonSerializer.Serialize(s));
        editor.MoveStage(last, 1).Should().BeTrue();
        editor.CurrentStage.Id.Should().Be(currentId); editor.Slots.Should().BeSameAs(slots);
        editor.PhaseLabels.Keys.First().Should().Be(LineupPhase.后期);
        editor.PhaseLabels.Keys.ElementAt(1).Should().Be(last);
        editor.MoveStage(last, editor.PhaseLabels.Count - 1).Should().BeTrue();
        editor.MoveStage(first, editor.PhaseLabels.Count - 1).Should().BeTrue();
        editor.MoveStage(first, 0).Should().BeTrue();
        editor.MoveStage(first, 1).Should().BeFalse();
        editor.MoveStage(LineupPhase.后期, 3).Should().BeFalse();
        var saved = editor.ToLineup(); var stages = saved.SklandGuide?.Stages ?? saved.EditorStages!;
        stages[0].IsFinal.Should().BeTrue();
        foreach (var stage in stages) JsonSerializer.Serialize(stage).Should().Be(contents[stage.Id]);
        saved.Slots.Should().BeEquivalentTo(before.Slots);
        var reopened = Open(await new LineupShareService(EquipmentTestData.Rules, Path.GetTempPath()).ImportAsync(JsonSerializer.Serialize(saved)));
        (reopened.ToLineup().SklandGuide?.Stages ?? reopened.ToLineup().EditorStages!).Select(s => s.Id).Should().Equal(stages.Select(s => s.Id));
        reopened.CurrentStage.IsFinal.Should().BeTrue();
        var tracker = new TrackingViewModel(); tracker.TrackLineup(saved);
        tracker.SetAutomaticStageSwitch(true);
        tracker.Economy = new ShopEconomy(14, 10, 3, false, null, true);
        tracker.SelectedStageChoice!.StageId.Should().Be(stages.Single(s => s.Name == "末段").Id);
        editor.Reset(); editor.PhaseLabels.Keys.Should().Equal(initialOrder);
        JsonSerializer.Serialize(source).Should().Be(json);
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "skland-guide-import.json"))) });
    }
}

