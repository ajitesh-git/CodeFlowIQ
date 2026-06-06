using CodeFlowIQ.Indexing;

namespace CodeFlowIQ.Tests;

public sealed class LanguageDetectorTests
{
    [Theory]
    [InlineData("OrderService.cs", "csharp")]
    [InlineData("schema.sql", "sql")]
    [InlineData("register.component.ts", "typescript")]
    [InlineData("App.tsx", "typescript")]
    [InlineData("README.md", "markdown")]
    public void Detect_ReturnsExpectedLanguage(string fileName, string expected)
    {
        var detector = new LanguageDetector();

        var actual = detector.Detect(fileName);

        Assert.Equal(expected, actual);
    }
}
