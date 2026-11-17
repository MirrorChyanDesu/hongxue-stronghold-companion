using System.Text.Json;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class UpdateLayoutTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("Hongxue-Layout-").FullName;

    [Theory]
    [InlineData("ASPAssistant.App.exe")]
    [InlineData("HongxueCompanion.exe")]
    [InlineData("app/HongxueCompanion.exe")]
    public void DeclaredLayout_ResolvesOnlyExistingMainExecutable(string executable)
    {
        var path = Path.Combine(_root, executable);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
        File.WriteAllText(Path.Combine(_root, "update-layout.json"),
            JsonSerializer.Serialize(new { formatVersion = 1, executable }));
        UpdateService.ResolvePackageExecutable(_root).Should().Be(executable);
    }

    [Fact]
    public void LegacyPackage_RequiresOriginalExecutable()
    {
        var resolve = () => UpdateService.ResolvePackageExecutable(_root);
        resolve.Should().Throw<InvalidDataException>().WithMessage("*ASPAssistant.App.exe*");
        File.WriteAllText(Path.Combine(_root, "ASPAssistant.App.exe"), "fixture");
        resolve().Should().Be("ASPAssistant.App.exe");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"formatVersion\":2,\"executable\":\"ASPAssistant.App.exe\"}")]
    [InlineData("{\"formatVersion\":1,\"executable\":\"../outside.exe\"}")]
    [InlineData("{\"formatVersion\":1,\"executable\":\"C:/outside.exe\"}")]
    [InlineData("{\"formatVersion\":1,\"executable\":\"app/../ASPAssistant.App.exe\"}")]
    [InlineData("{\"formatVersion\":1,\"executable\":\"鸿雪·卫戍助手.exe\"}")]
    public void UnsupportedLayout_IsRejectedEvenWhenLegacyExecutableExists(string json)
    {
        File.WriteAllText(Path.Combine(_root, "ASPAssistant.App.exe"), "fixture");
        File.WriteAllText(Path.Combine(_root, "update-layout.json"), json);
        var resolve = () => UpdateService.ResolvePackageExecutable(_root);
        resolve.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MalformedManifest_IsRejected()
    {
        File.WriteAllText(Path.Combine(_root, "update-layout.json"), "{");
        var resolve = () => UpdateService.ResolvePackageExecutable(_root);
        resolve.Should().Throw<JsonException>();
    }

    [Fact]
    public void MissingDeclaredExecutable_DoesNotFallBackToRootLauncher()
    {
        File.WriteAllText(Path.Combine(_root, "ASPAssistant.App.exe"), "fixture");
        File.WriteAllText(Path.Combine(_root, "update-layout.json"),
            "{\"formatVersion\":1,\"executable\":\"app/HongxueCompanion.exe\"}");
        var resolve = () => UpdateService.ResolvePackageExecutable(_root);
        resolve.Should().Throw<InvalidDataException>().WithMessage("*app/HongxueCompanion.exe*");
    }

    public void Dispose() => Directory.Delete(_root, true);
}
