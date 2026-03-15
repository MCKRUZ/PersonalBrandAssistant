using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;

public class TwitterContentFormatterTests
{
    private readonly TwitterContentFormatter _sut = new();

    [Fact]
    public void Platform_IsTwitterX() =>
        Assert.Equal(PlatformType.TwitterX, _sut.Platform);

    [Fact]
    public void FormatAndValidate_ShortText_ReturnsSingleTweet()
    {
        var content = Content.Create(ContentType.SocialPost, "Hello world!");

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello world!", result.Value!.Text);
        Assert.False(result.Value.Metadata.ContainsKey("thread:1"));
    }

    [Fact]
    public void FormatAndValidate_TextAt280Chars_NoTruncation()
    {
        var body = new string('A', 280);
        var content = Content.Create(ContentType.SocialPost, body);

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.Equal(280, result.Value!.Text.Length);
    }

    [Fact]
    public void FormatAndValidate_LongContent_SplitsIntoThread()
    {
        var body = string.Join(" ", Enumerable.Range(1, 80).Select(i => $"Sentence number {i}."));
        var content = Content.Create(ContentType.SocialPost, body);

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Metadata.ContainsKey("thread:1"));
        Assert.True(result.Value.Text.Length <= 280);
    }

    [Fact]
    public void FormatAndValidate_Thread_AddsNumbering()
    {
        var body = string.Join(" ", Enumerable.Range(1, 80).Select(i => $"Sentence number {i}."));
        var content = Content.Create(ContentType.SocialPost, body);

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);

        // Count total parts
        var threadCount = result.Value!.Metadata.Keys.Count(k => k.StartsWith("thread:")) + 1;
        Assert.True(threadCount >= 2);

        // First tweet should end with 1/N
        Assert.EndsWith($" 1/{threadCount}", result.Value.Text);

        // Second tweet should contain 2/N
        Assert.EndsWith($" 2/{threadCount}", result.Value.Metadata["thread:1"]);
    }

    [Fact]
    public void FormatAndValidate_Thread_EachPartWithinLimit()
    {
        var body = string.Join(" ", Enumerable.Range(1, 80).Select(i => $"Sentence number {i}."));
        var content = Content.Create(ContentType.SocialPost, body);

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Text.Length <= 280);

        foreach (var kv in result.Value.Metadata.Where(k => k.Key.StartsWith("thread:")))
        {
            Assert.True(kv.Value.Length <= 280, $"{kv.Key} exceeds 280 chars: {kv.Value.Length}");
        }
    }

    [Fact]
    public void FormatAndValidate_AppendsHashtags()
    {
        var content = Content.Create(ContentType.SocialPost, "Short post");
        content.Metadata.Tags.AddRange(["tech", "ai"]);

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.Contains("#tech", result.Value!.Text);
        Assert.Contains("#ai", result.Value!.Text);
        Assert.True(result.Value.Text.Length <= 280);
    }

    [Fact]
    public void FormatAndValidate_HashtagsDroppedIfExceedLimit()
    {
        var body = new string('A', 270);
        var content = Content.Create(ContentType.SocialPost, body);
        content.Metadata.Tags.AddRange(["verylonghashtagthatwontfit"]);

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        // Hashtag won't fit so should not be appended
        Assert.DoesNotContain("#verylonghashtagthatwontfit", result.Value!.Text);
    }

    [Fact]
    public void FormatAndValidate_EmptyBody_ReturnsFailure()
    {
        var content = Content.Create(ContentType.SocialPost, "");

        var result = _sut.FormatAndValidate(content);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void FormatAndValidate_WhitespaceBody_ReturnsFailure()
    {
        var content = Content.Create(ContentType.SocialPost, "   ");

        var result = _sut.FormatAndValidate(content);

        Assert.False(result.IsSuccess);
    }
}
