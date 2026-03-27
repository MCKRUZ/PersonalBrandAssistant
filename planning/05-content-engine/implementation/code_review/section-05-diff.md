diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IRepurposingService.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IRepurposingService.cs
new file mode 100644
index 0000000..230e6c9
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IRepurposingService.cs
@@ -0,0 +1,16 @@
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface IRepurposingService
+{
+    Task<Result<IReadOnlyList<Guid>>> RepurposeAsync(
+        Guid sourceContentId, PlatformType[] targetPlatforms, CancellationToken ct);
+
+    Task<Result<IReadOnlyList<RepurposingSuggestion>>> SuggestRepurposingAsync(
+        Guid contentId, CancellationToken ct);
+
+    Task<Result<IReadOnlyList<Content>>> GetContentTreeAsync(Guid rootId, CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs
new file mode 100644
index 0000000..7fa475c
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs
@@ -0,0 +1,8 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public class ContentEngineOptions
+{
+    public const string SectionName = "ContentEngine";
+
+    public int MaxTreeDepth { get; set; } = 3;
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/RepurposingSuggestion.cs b/src/PersonalBrandAssistant.Application/Common/Models/RepurposingSuggestion.cs
new file mode 100644
index 0000000..6d2d987
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/RepurposingSuggestion.cs
@@ -0,0 +1,9 @@
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record RepurposingSuggestion(
+    PlatformType Platform,
+    ContentType SuggestedType,
+    string Rationale,
+    float ConfidenceScore);
diff --git a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
index db6be9d..f465d6a 100644
--- a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
@@ -76,8 +76,11 @@ public static class DependencyInjection
         services.AddScoped<INotificationService, NotificationService>();
 
         // Content pipeline
+        services.Configure<ContentEngineOptions>(
+            configuration.GetSection(ContentEngineOptions.SectionName));
         services.AddScoped<IBrandVoiceService, StubBrandVoiceService>();
         services.AddScoped<IContentPipeline, ContentPipeline>();
+        services.AddScoped<IRepurposingService, RepurposingService>();
 
         // Platform integration options
         services.Configure<PlatformIntegrationOptions>(configuration.GetSection(PlatformIntegrationOptions.SectionName));
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/RepurposingService.cs b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/RepurposingService.cs
new file mode 100644
index 0000000..b0d7569
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/RepurposingService.cs
@@ -0,0 +1,246 @@
+using System.Text;
+using System.Text.Json;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+
+public sealed class RepurposingService : IRepurposingService
+{
+    private readonly IApplicationDbContext _dbContext;
+    private readonly ISidecarClient _sidecarClient;
+    private readonly ContentEngineOptions _options;
+    private readonly ILogger<RepurposingService> _logger;
+
+    private static readonly Dictionary<PlatformType, ContentType> DefaultPlatformMapping = new()
+    {
+        [PlatformType.TwitterX] = ContentType.Thread,
+        [PlatformType.LinkedIn] = ContentType.SocialPost,
+        [PlatformType.Instagram] = ContentType.SocialPost,
+        [PlatformType.YouTube] = ContentType.VideoDescription,
+    };
+
+    private static readonly JsonSerializerOptions JsonOptions = new()
+    {
+        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
+        PropertyNameCaseInsensitive = true,
+    };
+
+    public RepurposingService(
+        IApplicationDbContext dbContext,
+        ISidecarClient sidecarClient,
+        IOptions<ContentEngineOptions> options,
+        ILogger<RepurposingService> logger)
+    {
+        _dbContext = dbContext;
+        _sidecarClient = sidecarClient;
+        _options = options.Value;
+        _logger = logger;
+    }
+
+    public async Task<Result<IReadOnlyList<Guid>>> RepurposeAsync(
+        Guid sourceContentId, PlatformType[] targetPlatforms, CancellationToken ct)
+    {
+        var source = await _dbContext.Contents.FindAsync([sourceContentId], ct);
+        if (source is null)
+        {
+            return Result<IReadOnlyList<Guid>>.NotFound($"Content {sourceContentId} not found");
+        }
+
+        if (source.TreeDepth >= _options.MaxTreeDepth)
+        {
+            return Result<IReadOnlyList<Guid>>.Failure(
+                ErrorCode.ValidationFailed, "Maximum repurposing depth exceeded");
+        }
+
+        var sourcePlatform = source.TargetPlatforms.Length > 0
+            ? source.TargetPlatforms[0]
+            : (PlatformType?)null;
+
+        // Load existing children for idempotency check
+        var existingChildren = _dbContext.Contents
+            .Where(c => c.ParentContentId == sourceContentId)
+            .ToList();
+
+        var createdIds = new List<Guid>();
+
+        foreach (var targetPlatform in targetPlatforms)
+        {
+            var contentType = DefaultPlatformMapping.GetValueOrDefault(targetPlatform, ContentType.SocialPost);
+
+            // Idempotency: skip if child already exists for this combination
+            var alreadyExists = existingChildren.Any(c =>
+                c.RepurposeSourcePlatform == sourcePlatform &&
+                c.ContentType == contentType);
+
+            if (alreadyExists)
+            {
+                _logger.LogInformation(
+                    "Skipping repurpose for {Platform}/{ContentType} — child already exists for content {ContentId}",
+                    targetPlatform, contentType, sourceContentId);
+                continue;
+            }
+
+            var prompt = BuildRepurposePrompt(source, targetPlatform, contentType);
+            var (text, error) = await ConsumeTextAsync(prompt, ct);
+
+            if (text is null)
+            {
+                _logger.LogWarning(
+                    "Sidecar returned no text for repurpose {Platform}: {Error}",
+                    targetPlatform, error);
+                continue;
+            }
+
+            var child = Content.Create(
+                contentType,
+                body: text,
+                title: null,
+                targetPlatforms: [targetPlatform]);
+
+            child.ParentContentId = sourceContentId;
+            child.TreeDepth = source.TreeDepth + 1;
+            child.RepurposeSourcePlatform = sourcePlatform;
+
+            _dbContext.Contents.Add(child);
+            createdIds.Add(child.Id);
+        }
+
+        if (createdIds.Count > 0)
+        {
+            await _dbContext.SaveChangesAsync(ct);
+        }
+
+        return Result<IReadOnlyList<Guid>>.Success(createdIds);
+    }
+
+    public async Task<Result<IReadOnlyList<RepurposingSuggestion>>> SuggestRepurposingAsync(
+        Guid contentId, CancellationToken ct)
+    {
+        var content = await _dbContext.Contents.FindAsync([contentId], ct);
+        if (content is null)
+        {
+            return Result<IReadOnlyList<RepurposingSuggestion>>.NotFound($"Content {contentId} not found");
+        }
+
+        var prompt = $"""
+            Analyze this {content.ContentType} content and suggest platforms for repurposing.
+            Content: {content.Body}
+
+            Return a JSON array of suggestions, each with: platform (TwitterX|LinkedIn|Instagram|YouTube), suggestedType (BlogPost|SocialPost|Thread|VideoDescription), rationale, confidenceScore (0.0-1.0).
+            Return ONLY the JSON array, no other text.
+            """;
+
+        var (text, _) = await ConsumeTextAsync(prompt, ct);
+        if (text is null)
+        {
+            return Result<IReadOnlyList<RepurposingSuggestion>>.Failure(
+                ErrorCode.InternalError, "Sidecar returned no suggestions");
+        }
+
+        try
+        {
+            var raw = JsonSerializer.Deserialize<List<SuggestionDto>>(text, JsonOptions) ?? [];
+            var suggestions = raw
+                .Where(s => Enum.TryParse<PlatformType>(s.Platform, true, out _)
+                         && Enum.TryParse<ContentType>(s.SuggestedType, true, out _))
+                .Select(s => new RepurposingSuggestion(
+                    Enum.Parse<PlatformType>(s.Platform, true),
+                    Enum.Parse<ContentType>(s.SuggestedType, true),
+                    s.Rationale,
+                    s.ConfidenceScore))
+                .OrderByDescending(s => s.ConfidenceScore)
+                .ToList();
+
+            return Result<IReadOnlyList<RepurposingSuggestion>>.Success(suggestions);
+        }
+        catch (JsonException ex)
+        {
+            _logger.LogWarning(ex, "Failed to parse repurposing suggestions from sidecar");
+            return Result<IReadOnlyList<RepurposingSuggestion>>.Failure(
+                ErrorCode.InternalError, "Failed to parse AI suggestions");
+        }
+    }
+
+    public async Task<Result<IReadOnlyList<Content>>> GetContentTreeAsync(
+        Guid rootId, CancellationToken ct)
+    {
+        var root = await _dbContext.Contents.FindAsync([rootId], ct);
+        if (root is null)
+        {
+            return Result<IReadOnlyList<Content>>.NotFound($"Content {rootId} not found");
+        }
+
+        // Iterative BFS to collect all descendants
+        var allContents = _dbContext.Contents.ToList();
+        var descendants = new List<Content>();
+        var queue = new Queue<Guid>();
+        queue.Enqueue(rootId);
+
+        while (queue.Count > 0)
+        {
+            var parentId = queue.Dequeue();
+            var children = allContents.Where(c => c.ParentContentId == parentId).ToList();
+            foreach (var child in children)
+            {
+                descendants.Add(child);
+                queue.Enqueue(child.Id);
+            }
+        }
+
+        return Result<IReadOnlyList<Content>>.Success(descendants);
+    }
+
+    private static string BuildRepurposePrompt(Content source, PlatformType targetPlatform, ContentType contentType)
+    {
+        var builder = new StringBuilder();
+        builder.AppendLine($"Repurpose the following {source.ContentType} content for {targetPlatform} as a {contentType}.");
+        builder.AppendLine();
+        builder.AppendLine($"Source content:");
+        builder.AppendLine(source.Body);
+        builder.AppendLine();
+
+        builder.AppendLine(targetPlatform switch
+        {
+            PlatformType.TwitterX => "Format as a Twitter/X thread. Max 280 chars per tweet. Use numbered tweets.",
+            PlatformType.LinkedIn => "Format as a LinkedIn post. Professional tone. Max 3000 chars.",
+            PlatformType.Instagram => "Format as an Instagram caption. Include relevant hashtags. Max 2200 chars.",
+            PlatformType.YouTube => "Format as a YouTube video description with timestamps and links.",
+            _ => "Adapt the content appropriately for the target platform.",
+        });
+
+        return builder.ToString();
+    }
+
+    private async Task<(string? Text, string? Error)> ConsumeTextAsync(string prompt, CancellationToken ct)
+    {
+        var textBuilder = new StringBuilder();
+
+        await foreach (var evt in _sidecarClient.SendTaskAsync(prompt, null, null, ct))
+        {
+            switch (evt)
+            {
+                case ChatEvent { Text: not null } chat:
+                    textBuilder.Append(chat.Text);
+                    break;
+                case ErrorEvent error:
+                    _logger.LogError("Sidecar error during repurposing: {Message}", error.Message);
+                    return (null, error.Message);
+            }
+        }
+
+        var text = textBuilder.Length > 0 ? textBuilder.ToString() : null;
+        return (text, null);
+    }
+
+    private record SuggestionDto(
+        string Platform,
+        string SuggestedType,
+        string Rationale,
+        float ConfidenceScore);
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/RepurposingServiceTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/RepurposingServiceTests.cs
new file mode 100644
index 0000000..4371aa3
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/RepurposingServiceTests.cs
@@ -0,0 +1,264 @@
+using System.Runtime.CompilerServices;
+using System.Text.Json;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentServices;
+
+public class RepurposingServiceTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+    private readonly Mock<ISidecarClient> _sidecarClient = new();
+    private readonly Mock<ILogger<RepurposingService>> _logger = new();
+    private readonly IOptions<ContentEngineOptions> _options =
+        Options.Create(new ContentEngineOptions { MaxTreeDepth = 3 });
+
+    private RepurposingService CreateSut() =>
+        new(_dbContext.Object, _sidecarClient.Object, _options, _logger.Object);
+
+    private Content CreateSourceContent(
+        ContentType type = ContentType.BlogPost,
+        int treeDepth = 0,
+        PlatformType[]? targetPlatforms = null)
+    {
+        var content = Content.Create(type, "Source body", "Source Title", targetPlatforms ?? [PlatformType.LinkedIn]);
+        content.TreeDepth = treeDepth;
+        return content;
+    }
+
+    private void SetupContentsDbSet(params Content[] contents)
+    {
+        var mock = contents.AsQueryable().BuildMockDbSet();
+        mock.Setup(d => d.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
+            .Returns<object[], CancellationToken>((keys, _) =>
+                ValueTask.FromResult(contents.FirstOrDefault(c => c.Id == (Guid)keys[0])));
+        mock.Setup(d => d.Add(It.IsAny<Content>()));
+        _dbContext.Setup(d => d.Contents).Returns(mock.Object);
+    }
+
+    private void SetupSidecarResponse(string text)
+    {
+        _sidecarClient
+            .Setup(s => s.SendTaskAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .Returns(ToAsyncEnumerable(
+                new ChatEvent("text", text, null, null),
+                new TaskCompleteEvent("session", 100, 50)));
+    }
+
+    private static async IAsyncEnumerable<SidecarEvent> ToAsyncEnumerable(
+        params SidecarEvent[] events)
+    {
+        foreach (var evt in events)
+        {
+            yield return evt;
+        }
+        await Task.CompletedTask;
+    }
+
+    // --- RepurposeAsync tests ---
+
+    [Fact]
+    public async Task RepurposeAsync_WithValidSource_CreatesChildContentForEachTargetPlatform()
+    {
+        var source = CreateSourceContent();
+        SetupContentsDbSet(source);
+        SetupSidecarResponse("Repurposed content");
+
+        var sut = CreateSut();
+        var result = await sut.RepurposeAsync(source.Id, [PlatformType.TwitterX, PlatformType.Instagram], CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(2, result.Value!.Count);
+        _dbContext.Verify(d => d.Contents.Add(It.IsAny<Content>()), Times.Exactly(2));
+        _dbContext.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task RepurposeAsync_SetsParentContentIdOnChildren()
+    {
+        var source = CreateSourceContent();
+        SetupContentsDbSet(source);
+        SetupSidecarResponse("Repurposed content");
+
+        var addedContents = new List<Content>();
+        _dbContext.Setup(d => d.Contents.Add(It.IsAny<Content>()))
+            .Callback<Content>(c => addedContents.Add(c));
+
+        var sut = CreateSut();
+        await sut.RepurposeAsync(source.Id, [PlatformType.TwitterX], CancellationToken.None);
+
+        Assert.Single(addedContents);
+        Assert.Equal(source.Id, addedContents[0].ParentContentId);
+    }
+
+    [Fact]
+    public async Task RepurposeAsync_SetsRepurposeSourcePlatform()
+    {
+        var source = CreateSourceContent(targetPlatforms: [PlatformType.LinkedIn]);
+        SetupContentsDbSet(source);
+        SetupSidecarResponse("Repurposed content");
+
+        var addedContents = new List<Content>();
+        _dbContext.Setup(d => d.Contents.Add(It.IsAny<Content>()))
+            .Callback<Content>(c => addedContents.Add(c));
+
+        var sut = CreateSut();
+        await sut.RepurposeAsync(source.Id, [PlatformType.TwitterX], CancellationToken.None);
+
+        Assert.Single(addedContents);
+        Assert.Equal(PlatformType.LinkedIn, addedContents[0].RepurposeSourcePlatform);
+    }
+
+    [Fact]
+    public async Task RepurposeAsync_SetsTreeDepthIncremented()
+    {
+        var source = CreateSourceContent(treeDepth: 1);
+        SetupContentsDbSet(source);
+        SetupSidecarResponse("Repurposed content");
+
+        var addedContents = new List<Content>();
+        _dbContext.Setup(d => d.Contents.Add(It.IsAny<Content>()))
+            .Callback<Content>(c => addedContents.Add(c));
+
+        var sut = CreateSut();
+        await sut.RepurposeAsync(source.Id, [PlatformType.TwitterX], CancellationToken.None);
+
+        Assert.Equal(2, addedContents[0].TreeDepth);
+    }
+
+    [Fact]
+    public async Task RepurposeAsync_RespectsMaxTreeDepth_FailsIfExceeded()
+    {
+        var source = CreateSourceContent(treeDepth: 3);
+        SetupContentsDbSet(source);
+
+        var sut = CreateSut();
+        var result = await sut.RepurposeAsync(source.Id, [PlatformType.TwitterX], CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task RepurposeAsync_IsIdempotent_SkipsExistingChildForSameParentPlatformType()
+    {
+        var source = CreateSourceContent();
+        var existingChild = Content.Create(ContentType.Thread, "Existing", targetPlatforms: [PlatformType.TwitterX]);
+        existingChild.ParentContentId = source.Id;
+        existingChild.RepurposeSourcePlatform = PlatformType.LinkedIn;
+
+        SetupContentsDbSet(source, existingChild);
+        SetupSidecarResponse("New content");
+
+        var sut = CreateSut();
+        var result = await sut.RepurposeAsync(source.Id, [PlatformType.TwitterX, PlatformType.Instagram], CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        // Only Instagram should be created; TwitterX already exists
+        Assert.Single(result.Value!);
+        _dbContext.Verify(d => d.Contents.Add(It.IsAny<Content>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task RepurposeAsync_ContentNotFound_ReturnsNotFound()
+    {
+        SetupContentsDbSet();
+
+        var sut = CreateSut();
+        var result = await sut.RepurposeAsync(Guid.NewGuid(), [PlatformType.TwitterX], CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+
+    // --- SuggestRepurposingAsync tests ---
+
+    [Fact]
+    public async Task SuggestRepurposingAsync_ReturnsSuggestionsWithConfidenceScores()
+    {
+        var source = CreateSourceContent();
+        SetupContentsDbSet(source);
+
+        var suggestionsJson = JsonSerializer.Serialize(new[]
+        {
+            new { platform = "TwitterX", suggestedType = "Thread", rationale = "Great for threads", confidenceScore = 0.9f },
+            new { platform = "LinkedIn", suggestedType = "SocialPost", rationale = "Professional audience", confidenceScore = 0.7f },
+        });
+        SetupSidecarResponse(suggestionsJson);
+
+        var sut = CreateSut();
+        var result = await sut.SuggestRepurposingAsync(source.Id, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(2, result.Value!.Count);
+        Assert.Equal(PlatformType.TwitterX, result.Value[0].Platform);
+        Assert.True(result.Value[0].ConfidenceScore >= result.Value[1].ConfidenceScore);
+    }
+
+    [Fact]
+    public async Task SuggestRepurposingAsync_ContentNotFound_ReturnsNotFound()
+    {
+        SetupContentsDbSet();
+
+        var sut = CreateSut();
+        var result = await sut.SuggestRepurposingAsync(Guid.NewGuid(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+
+    // --- GetContentTreeAsync tests ---
+
+    [Fact]
+    public async Task GetContentTreeAsync_ReturnsFullDescendantTree()
+    {
+        var root = CreateSourceContent();
+        var childA = Content.Create(ContentType.Thread, "Child A", targetPlatforms: [PlatformType.TwitterX]);
+        childA.ParentContentId = root.Id;
+        var childC = Content.Create(ContentType.SocialPost, "Child C", targetPlatforms: [PlatformType.LinkedIn]);
+        childC.ParentContentId = root.Id;
+        var grandchildB = Content.Create(ContentType.SocialPost, "Grandchild B", targetPlatforms: [PlatformType.Instagram]);
+        grandchildB.ParentContentId = childA.Id;
+
+        SetupContentsDbSet(root, childA, childC, grandchildB);
+
+        var sut = CreateSut();
+        var result = await sut.GetContentTreeAsync(root.Id, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(3, result.Value!.Count);
+    }
+
+    [Fact]
+    public async Task GetContentTreeAsync_RootNotFound_ReturnsNotFound()
+    {
+        SetupContentsDbSet();
+
+        var sut = CreateSut();
+        var result = await sut.GetContentTreeAsync(Guid.NewGuid(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task GetContentTreeAsync_NoChildren_ReturnsEmptyList()
+    {
+        var root = CreateSourceContent();
+        SetupContentsDbSet(root);
+
+        var sut = CreateSut();
+        var result = await sut.GetContentTreeAsync(root.Id, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Empty(result.Value!);
+    }
+}
