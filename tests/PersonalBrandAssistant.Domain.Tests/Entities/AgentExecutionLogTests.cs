using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class AgentExecutionLogTests
{
    private static readonly Guid TestExecutionId = Guid.NewGuid();

    [Fact]
    public void Create_SetsIdAsNonEmptyGuid()
    {
        var log = AgentExecutionLog.Create(TestExecutionId, 1, "prompt", "test content", 100);
        Assert.NotEqual(Guid.Empty, log.Id);
    }

    [Fact]
    public void Create_SetsAllPropertiesCorrectly()
    {
        var log = AgentExecutionLog.Create(TestExecutionId, 2, "completion", "response text", 250);

        Assert.Equal(TestExecutionId, log.AgentExecutionId);
        Assert.Equal(2, log.StepNumber);
        Assert.Equal("completion", log.StepType);
        Assert.Equal("response text", log.Content);
        Assert.Equal(250, log.TokensUsed);
    }

    [Fact]
    public void Create_SetsTimestampToCurrentTime()
    {
        var before = DateTimeOffset.UtcNow;
        var log = AgentExecutionLog.Create(TestExecutionId, 1, "prompt", "content", 50);
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(log.Timestamp, before, after);
    }

    [Fact]
    public void Create_WithContentLongerThan2000Chars_TruncatesTo2000()
    {
        var longContent = new string('x', 3000);
        var log = AgentExecutionLog.Create(TestExecutionId, 1, "prompt", longContent, 100);

        Assert.Equal(2000, log.Content!.Length);
    }

    [Fact]
    public void Create_WithContentAtExactly2000Chars_StoresAsIs()
    {
        var content = new string('x', 2000);
        var log = AgentExecutionLog.Create(TestExecutionId, 1, "prompt", content, 100);

        Assert.Equal(2000, log.Content!.Length);
    }

    [Fact]
    public void Create_WithContentShorterThan2000Chars_StoresAsIs()
    {
        var content = "short content";
        var log = AgentExecutionLog.Create(TestExecutionId, 1, "prompt", content, 100);

        Assert.Equal("short content", log.Content);
    }

    [Fact]
    public void Create_WithNullContent_StoresNull()
    {
        var log = AgentExecutionLog.Create(TestExecutionId, 1, "prompt", null, 100);
        Assert.Null(log.Content);
    }
}
