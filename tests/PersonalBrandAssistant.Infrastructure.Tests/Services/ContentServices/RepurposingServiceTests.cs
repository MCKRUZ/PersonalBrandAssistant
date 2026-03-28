using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentServices;

public class RepurposingServiceTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<ISidecarClient> _sidecarClient = new();
    private readonly Mock<ILogger<RepurposingService>> _logger = new();
    private readonly IOptions<ContentEngineOptions> _options =
        Options.Create(new ContentEngineOptions { MaxTreeDepth = 3 });

    private RepurposingService CreateSut() =>
        new(_dbContext.Object, _sidecarClient.Object, _options, _logger.Object);

    private Content CreateSourceContent(
        ContentType type = ContentType.BlogPost,
        int treeDepth = 0,
        PlatformType[]? targetPlatforms = null)
    {
        var content = Content.Create(type, "Source body", "Source Title", targetPlatforms ?? [PlatformType.LinkedIn]);
        content.TreeDepth = treeDepth;
        return content;
    }

    private void SetupContentsDbSet(params Content[] contents)
    {
        var mock = contents.AsQueryable().BuildMockDbSet();
        mock.Setup(d => d.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns<object[], CancellationToken>((keys, _) =>
                ValueTask.FromResult(contents.FirstOrDefault(c => c.Id == (Guid)keys[0])));
        mock.Setup(d => d.Add(It.IsAny<Content>()));
        _dbContext.Setup(d => d.Contents).Returns(mock.Object);
    }

    private void SetupSidecarResponse(string text)
    {
        _sidecarClient
            .Setup(s => s.SendTaskAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(
                new ChatEvent("text", text, null, null),
                new TaskCompleteEvent("session", 100, 50)));
    }

    private static async IAsyncEnumerable<SidecarEvent> ToAsyncEnumerable(
        params SidecarEvent[] events)
    {
        foreach (var evt in events)
        {
            yield return evt;
        }
        await Task.CompletedTask;
    }

    // --- RepurposeAsync tests ---

    [Fact]
    public async Task RepurposeAsync_WithValidSource_CreatesChildContentForEachTargetPlatform()
    {
        var source = CreateSourceContent();
        SetupContentsDbSet(source);
        SetupSidecarResponse("Repurposed content");

        var sut = CreateSut();
        var result = await sut.RepurposeAsync(source.Id, [PlatformType.TwitterX, PlatformType.Instagram], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        _dbContext.Verify(d => d.Contents.Add(It.IsAny<Content>()), Times.Exactly(2));
        _dbContext.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RepurposeAsync_SetsParentContentIdOnChildren()
    {
        var source = CreateSourceContent();
        SetupContentsDbSet(source);
        SetupSidecarResponse("Repurposed content");

        var addedContents = new List<Content>();
        _dbContext.Setup(d => d.Contents.Add(It.IsAny<Content>()))
            .Callback<Content>(c => addedContents.Add(c));

        var sut = CreateSut();
        await sut.RepurposeAsync(source.Id, [PlatformType.TwitterX], CancellationToken.None);

        Assert.Single(addedContents);
        Assert.Equal(source.Id, addedContents[0].ParentContentId);
    }

    [Fact]
    public async Task RepurposeAsync_SetsRepurposeSourcePlatform()
    {
        var source = CreateSourceContent(targetPlatforms: [PlatformType.LinkedIn]);
        SetupContentsDbSet(source);
        SetupSidecarResponse("Repurposed content");

        var addedContents = new List<Content>();
        _dbContext.Setup(d => d.Contents.Add(It.IsAny<Content>()))
            .Callback<Content>(c => addedContents.Add(c));

        var sut = CreateSut();
        await sut.RepurposeAsync(source.Id, [PlatformType.TwitterX], CancellationToken.None);

        Assert.Single(addedContents);
        Assert.Equal(PlatformType.LinkedIn, addedContents[0].RepurposeSourcePlatform);
    }

    [Fact]
    public async Task RepurposeAsync_SetsTreeDepthIncremented()
    {
        var source = CreateSourceContent(treeDepth: 1);
        SetupContentsDbSet(source);
        SetupSidecarResponse("Repurposed content");

        var addedContents = new List<Content>();
        _dbContext.Setup(d => d.Contents.Add(It.IsAny<Content>()))
            .Callback<Content>(c => addedContents.Add(c));

        var sut = CreateSut();
        await sut.RepurposeAsync(source.Id, [PlatformType.TwitterX], CancellationToken.None);

        Assert.Equal(2, addedContents[0].TreeDepth);
    }

    [Fact]
    public async Task RepurposeAsync_RespectsMaxTreeDepth_FailsIfExceeded()
    {
        var source = CreateSourceContent(treeDepth: 3);
        SetupContentsDbSet(source);

        var sut = CreateSut();
        var result = await sut.RepurposeAsync(source.Id, [PlatformType.TwitterX], CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task RepurposeAsync_IsIdempotent_SkipsExistingChildForSameParentPlatformType()
    {
        var source = CreateSourceContent();
        var existingChild = Content.Create(ContentType.Thread, "Existing", targetPlatforms: [PlatformType.TwitterX]);
        existingChild.ParentContentId = source.Id;
        existingChild.RepurposeSourcePlatform = PlatformType.LinkedIn;

        SetupContentsDbSet(source, existingChild);
        SetupSidecarResponse("New content");

        var sut = CreateSut();
        var result = await sut.RepurposeAsync(source.Id, [PlatformType.TwitterX, PlatformType.Instagram], CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Only Instagram should be created; TwitterX already exists
        Assert.Single(result.Value!);
        _dbContext.Verify(d => d.Contents.Add(It.IsAny<Content>()), Times.Once);
    }

    [Fact]
    public async Task RepurposeAsync_ContentNotFound_ReturnsNotFound()
    {
        SetupContentsDbSet();

        var sut = CreateSut();
        var result = await sut.RepurposeAsync(Guid.NewGuid(), [PlatformType.TwitterX], CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    // --- SuggestRepurposingAsync tests ---

    [Fact]
    public async Task SuggestRepurposingAsync_ReturnsSuggestionsWithConfidenceScores()
    {
        var source = CreateSourceContent();
        SetupContentsDbSet(source);

        var suggestionsJson = JsonSerializer.Serialize(new[]
        {
            new { platform = "TwitterX", suggestedType = "Thread", rationale = "Great for threads", confidenceScore = 0.9f },
            new { platform = "LinkedIn", suggestedType = "SocialPost", rationale = "Professional audience", confidenceScore = 0.7f },
        });
        SetupSidecarResponse(suggestionsJson);

        var sut = CreateSut();
        var result = await sut.SuggestRepurposingAsync(source.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal(PlatformType.TwitterX, result.Value[0].Platform);
        Assert.True(result.Value[0].ConfidenceScore >= result.Value[1].ConfidenceScore);
    }

    [Fact]
    public async Task SuggestRepurposingAsync_ContentNotFound_ReturnsNotFound()
    {
        SetupContentsDbSet();

        var sut = CreateSut();
        var result = await sut.SuggestRepurposingAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    // --- GetContentTreeAsync tests ---

    [Fact]
    public async Task GetContentTreeAsync_ReturnsFullDescendantTree()
    {
        var root = CreateSourceContent();
        var childA = Content.Create(ContentType.Thread, "Child A", targetPlatforms: [PlatformType.TwitterX]);
        childA.ParentContentId = root.Id;
        var childC = Content.Create(ContentType.SocialPost, "Child C", targetPlatforms: [PlatformType.LinkedIn]);
        childC.ParentContentId = root.Id;
        var grandchildB = Content.Create(ContentType.SocialPost, "Grandchild B", targetPlatforms: [PlatformType.Instagram]);
        grandchildB.ParentContentId = childA.Id;

        SetupContentsDbSet(root, childA, childC, grandchildB);

        var sut = CreateSut();
        var result = await sut.GetContentTreeAsync(root.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
    }

    [Fact]
    public async Task GetContentTreeAsync_RootNotFound_ReturnsNotFound()
    {
        SetupContentsDbSet();

        var sut = CreateSut();
        var result = await sut.GetContentTreeAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task GetContentTreeAsync_NoChildren_ReturnsEmptyList()
    {
        var root = CreateSourceContent();
        SetupContentsDbSet(root);

        var sut = CreateSut();
        var result = await sut.GetContentTreeAsync(root.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }
}
