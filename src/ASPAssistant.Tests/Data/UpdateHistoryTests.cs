using ASPAssistant.Core.Data;
using FluentAssertions;

namespace ASPAssistant.Tests.Data;

public sealed class UpdateHistoryTests
{
    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"Version\":\"bad\",\"PublishedDate\":\"2026-11-18\",\"Title\":\"x\",\"Content\":\"x\"}]")]
    public void InvalidHistoryCannotAppearAsAnEmptySuccess(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            Action load = () => UpdateHistory.Load(path);
            load.Should().Throw<InvalidDataException>();
        }
        finally { File.Delete(path); }
    }
}
