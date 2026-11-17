namespace ASPAssistant.Tests;

internal static class TestOutput
{
    static TestOutput() => System.IO.Directory.CreateDirectory(Directory);

    public static string Directory { get; } = Path.GetFullPath(
        Environment.GetEnvironmentVariable("ASPASSISTANT_TEST_RESULTS_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "TestResults"));
}
