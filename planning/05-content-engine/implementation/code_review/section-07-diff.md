diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IBrandVoiceService.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IBrandVoiceService.cs
index 4a4cde7..78cf44d 100644
--- a/src/PersonalBrandAssistant.Application/Common/Interfaces/IBrandVoiceService.cs
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IBrandVoiceService.cs
@@ -1,8 +1,14 @@
 using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
 
 namespace PersonalBrandAssistant.Application.Common.Interfaces;
 
 public interface IBrandVoiceService
 {
     Task<Result<BrandVoiceScore>> ScoreContentAsync(Guid contentId, CancellationToken ct);
+
+    Task<Result<MediatR.Unit>> ValidateAndGateAsync(Guid contentId, AutonomyLevel autonomy, CancellationToken ct);
+
+    Result<IReadOnlyList<string>> RunRuleChecks(string text, BrandProfile profile);
 }
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs
index 7fa475c..52e43f8 100644
--- a/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs
+++ b/src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs
@@ -5,4 +5,8 @@ public class ContentEngineOptions
     public const string SectionName = "ContentEngine";
 
     public int MaxTreeDepth { get; set; } = 3;
+
+    public int BrandVoiceScoreThreshold { get; set; } = 70;
+
+    public int MaxAutoRegenerateAttempts { get; set; } = 3;
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
index ef50eab..dae3ff3 100644
--- a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
@@ -78,7 +78,7 @@ public static class DependencyInjection
         // Content pipeline
         services.Configure<ContentEngineOptions>(
             configuration.GetSection(ContentEngineOptions.SectionName));
-        services.AddScoped<IBrandVoiceService, StubBrandVoiceService>();
+        services.AddScoped<IBrandVoiceService, BrandVoiceService>();
         services.AddScoped<IContentPipeline, ContentPipeline>();
         services.AddScoped<IRepurposingService, RepurposingService>();
         services.AddScoped<IContentCalendarService, ContentCalendarService>();
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/BrandVoiceService.cs b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/BrandVoiceService.cs
new file mode 100644
index 0000000..1998069
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/BrandVoiceService.cs
@@ -0,0 +1,254 @@
+using System.Net;
+using System.Text.Json;
+using System.Text.Json.Serialization;
+using System.Text.RegularExpressions;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
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
+public sealed class BrandVoiceService : IBrandVoiceService
+{
+    private static readonly JsonSerializerOptions JsonOptions = new()
+    {
+        PropertyNameCaseInsensitive = true,
+    };
+
+    private readonly IApplicationDbContext _dbContext;
+    private readonly ISidecarClient _sidecar;
+    private readonly IServiceProvider _serviceProvider;
+    private readonly ContentEngineOptions _options;
+    private readonly ILogger<BrandVoiceService> _logger;
+
+    public BrandVoiceService(
+        IApplicationDbContext dbContext,
+        ISidecarClient sidecar,
+        IServiceProvider serviceProvider,
+        IOptions<ContentEngineOptions> options,
+        ILogger<BrandVoiceService> logger)
+    {
+        _dbContext = dbContext;
+        _sidecar = sidecar;
+        _serviceProvider = serviceProvider;
+        _options = options.Value;
+        _logger = logger;
+    }
+
+    public Result<IReadOnlyList<string>> RunRuleChecks(string text, BrandProfile profile)
+    {
+        var violations = new List<string>();
+        var plainText = StripHtml(text);
+        var normalized = plainText.ToLowerInvariant();
+
+        foreach (var term in profile.VocabularyPreferences.AvoidTerms)
+        {
+            if (normalized.Contains(term.ToLowerInvariant()))
+            {
+                violations.Add($"Avoided term detected: '{term}'");
+            }
+        }
+
+        if (profile.VocabularyPreferences.PreferredTerms.Count > 0)
+        {
+            var hasPreferred = profile.VocabularyPreferences.PreferredTerms
+                .Any(t => normalized.Contains(t.ToLowerInvariant()));
+
+            if (!hasPreferred)
+            {
+                violations.Add(
+                    $"No preferred brand terms found. Consider including: {string.Join(", ", profile.VocabularyPreferences.PreferredTerms)}");
+            }
+        }
+
+        return Result<IReadOnlyList<string>>.Success(violations);
+    }
+
+    public async Task<Result<BrandVoiceScore>> ScoreContentAsync(Guid contentId, CancellationToken ct)
+    {
+        var content = await _dbContext.Contents.FindAsync([contentId], ct);
+        if (content is null)
+            return Result<BrandVoiceScore>.NotFound($"Content {contentId} not found");
+
+        var profile = await _dbContext.BrandProfiles
+            .FirstOrDefaultAsync(p => p.IsActive, ct);
+        if (profile is null)
+            return Result<BrandVoiceScore>.Failure(ErrorCode.ValidationFailed, "No active brand profile found");
+
+        var ruleViolations = RunRuleChecks(content.Body, profile).Value ?? [];
+
+        var prompt = BuildScoringPrompt(content.Body, profile);
+        var (responseText, error) = await ConsumeEventStreamAsync(prompt, ct);
+
+        if (error is not null)
+            return Result<BrandVoiceScore>.Failure(ErrorCode.ValidationFailed, error);
+
+        var dto = ParseScoreJson(responseText ?? "");
+        if (dto is null)
+            return Result<BrandVoiceScore>.Failure(ErrorCode.ValidationFailed,
+                "Failed to parse brand voice score from LLM response");
+
+        var score = new BrandVoiceScore(
+            dto.OverallScore,
+            dto.ToneAlignment,
+            dto.VocabularyConsistency,
+            dto.PersonaFidelity,
+            dto.Issues ?? [],
+            ruleViolations);
+
+        content.Metadata.PlatformSpecificData["BrandVoiceScore"] =
+            JsonSerializer.Serialize(score, JsonOptions);
+        await _dbContext.SaveChangesAsync(ct);
+
+        return Result<BrandVoiceScore>.Success(score);
+    }
+
+    public async Task<Result<MediatR.Unit>> ValidateAndGateAsync(
+        Guid contentId, AutonomyLevel autonomy, CancellationToken ct)
+    {
+        var scoreResult = await ScoreContentAsync(contentId, ct);
+        if (!scoreResult.IsSuccess)
+            return Result<MediatR.Unit>.Failure(scoreResult.ErrorCode, scoreResult.Errors.ToArray());
+
+        if (autonomy != AutonomyLevel.Autonomous)
+            return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
+
+        var threshold = _options.BrandVoiceScoreThreshold;
+        var maxAttempts = _options.MaxAutoRegenerateAttempts;
+
+        if (scoreResult.Value!.OverallScore >= threshold)
+            return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
+
+        var pipeline = _serviceProvider.GetRequiredService<IContentPipeline>();
+
+        for (var attempt = 0; attempt < maxAttempts; attempt++)
+        {
+            _logger.LogInformation(
+                "Brand voice score {Score} below threshold {Threshold} for content {ContentId}, regenerating (attempt {Attempt}/{Max})",
+                scoreResult.Value!.OverallScore, threshold, contentId, attempt + 1, maxAttempts);
+
+            await pipeline.GenerateDraftAsync(contentId, ct);
+
+            scoreResult = await ScoreContentAsync(contentId, ct);
+            if (!scoreResult.IsSuccess)
+                return Result<MediatR.Unit>.Failure(scoreResult.ErrorCode, scoreResult.Errors.ToArray());
+
+            if (scoreResult.Value!.OverallScore >= threshold)
+                return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
+        }
+
+        return Result<MediatR.Unit>.Failure(ErrorCode.ValidationFailed,
+            $"Brand voice score {scoreResult.Value!.OverallScore} still below threshold {threshold} after {maxAttempts} regeneration attempts");
+    }
+
+    private static string BuildScoringPrompt(string contentBody, BrandProfile profile)
+    {
+        var plainText = StripHtml(contentBody);
+        var tone = string.Join(", ", profile.ToneDescriptors);
+        var preferred = string.Join(", ", profile.VocabularyPreferences.PreferredTerms);
+        var avoided = string.Join(", ", profile.VocabularyPreferences.AvoidTerms);
+
+        return $$"""
+            You are a brand voice evaluator. Score the following content against the provided brand profile.
+            Return ONLY valid JSON, no markdown fencing, no explanation.
+
+            Brand Profile:
+            - Tone: {{tone}}
+            - Persona: {{profile.PersonaDescription}}
+            - Style Guidelines: {{profile.StyleGuidelines}}
+            - Preferred Terms: {{preferred}}
+            - Avoided Terms: {{avoided}}
+
+            Content to evaluate:
+            {{plainText}}
+
+            Expected JSON schema:
+            {"overallScore": 0, "toneAlignment": 0, "vocabularyConsistency": 0, "personaFidelity": 0, "issues": []}
+
+            Each dimension is 0-100. "issues" is an array of strings describing specific concerns.
+            """;
+    }
+
+    private async Task<(string? Text, string? Error)> ConsumeEventStreamAsync(
+        string prompt, CancellationToken ct)
+    {
+        try
+        {
+            var textParts = new List<string>();
+            await foreach (var evt in _sidecar.SendTaskAsync(prompt, null, null, ct))
+            {
+                switch (evt)
+                {
+                    case ChatEvent { Text: not null } chat:
+                        textParts.Add(chat.Text);
+                        break;
+                    case ErrorEvent error:
+                        return (null, error.Message);
+                }
+            }
+
+            return (string.Join("", textParts), null);
+        }
+        catch (Exception ex)
+        {
+            _logger.LogWarning(ex, "Error consuming sidecar event stream");
+            return (null, ex.Message);
+        }
+    }
+
+    private static BrandVoiceScoreDto? ParseScoreJson(string text)
+    {
+        // Strip markdown code fences if present
+        var cleaned = text.Trim();
+        if (cleaned.StartsWith("```"))
+        {
+            var firstNewline = cleaned.IndexOf('\n');
+            if (firstNewline >= 0)
+                cleaned = cleaned[(firstNewline + 1)..];
+            if (cleaned.EndsWith("```"))
+                cleaned = cleaned[..^3];
+            cleaned = cleaned.Trim();
+        }
+
+        try
+        {
+            return JsonSerializer.Deserialize<BrandVoiceScoreDto>(cleaned, JsonOptions);
+        }
+        catch (JsonException)
+        {
+            return null;
+        }
+    }
+
+    private static string StripHtml(string html)
+    {
+        var stripped = Regex.Replace(html, "<[^>]+>", " ");
+        stripped = WebUtility.HtmlDecode(stripped);
+        stripped = Regex.Replace(stripped, @"\s+", " ");
+        return stripped.Trim();
+    }
+
+    private sealed class BrandVoiceScoreDto
+    {
+        [JsonPropertyName("overallScore")]
+        public int OverallScore { get; set; }
+
+        [JsonPropertyName("toneAlignment")]
+        public int ToneAlignment { get; set; }
+
+        [JsonPropertyName("vocabularyConsistency")]
+        public int VocabularyConsistency { get; set; }
+
+        [JsonPropertyName("personaFidelity")]
+        public int PersonaFidelity { get; set; }
+
+        [JsonPropertyName("issues")]
+        public List<string>? Issues { get; set; }
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/StubBrandVoiceService.cs b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/StubBrandVoiceService.cs
index 0f832d6..14df543 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/StubBrandVoiceService.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/StubBrandVoiceService.cs
@@ -1,10 +1,12 @@
 using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
 
 namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;
 
 /// <summary>
-/// Placeholder until section-07 (Brand Voice) is implemented.
+/// Placeholder for testing scenarios that don't need real brand voice validation.
 /// Returns a perfect score so the pipeline can function end-to-end.
 /// </summary>
 public sealed class StubBrandVoiceService : IBrandVoiceService
@@ -14,4 +16,14 @@ public sealed class StubBrandVoiceService : IBrandVoiceService
         var score = new BrandVoiceScore(100, 100, 100, 100, [], []);
         return Task.FromResult(Result<BrandVoiceScore>.Success(score));
     }
+
+    public Task<Result<MediatR.Unit>> ValidateAndGateAsync(Guid contentId, AutonomyLevel autonomy, CancellationToken ct)
+    {
+        return Task.FromResult(Result<MediatR.Unit>.Success(MediatR.Unit.Value));
+    }
+
+    public Result<IReadOnlyList<string>> RunRuleChecks(string text, BrandProfile profile)
+    {
+        return Result<IReadOnlyList<string>>.Success([]);
+    }
 }
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Features/BrandVoice/BrandVoiceServiceTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Features/BrandVoice/BrandVoiceServiceTests.cs
new file mode 100644
index 0000000..863b9db
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Features/BrandVoice/BrandVoiceServiceTests.cs
@@ -0,0 +1,372 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Domain.ValueObjects;
+using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+using System.Text.Json;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Application.Tests.Services.BrandVoice;
+
+public class BrandVoiceServiceTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+    private readonly Mock<ISidecarClient> _sidecar = new();
+    private readonly Mock<IContentPipeline> _pipeline = new();
+    private readonly Mock<IServiceProvider> _serviceProvider = new();
+    private readonly Mock<ILogger<BrandVoiceService>> _logger = new();
+    private readonly ContentEngineOptions _options = new();
+
+    public BrandVoiceServiceTests()
+    {
+        _serviceProvider.Setup(sp => sp.GetService(typeof(IContentPipeline)))
+            .Returns(_pipeline.Object);
+    }
+
+    private BrandVoiceService CreateSut() => new(
+        _dbContext.Object,
+        _sidecar.Object,
+        _serviceProvider.Object,
+        Options.Create(_options),
+        _logger.Object);
+
+    private BrandProfile CreateProfile(
+        List<string>? avoidTerms = null,
+        List<string>? preferredTerms = null) => new()
+    {
+        Name = "Test Profile",
+        ToneDescriptors = ["professional", "friendly"],
+        StyleGuidelines = "Be concise.",
+        VocabularyPreferences = new VocabularyConfig
+        {
+            AvoidTerms = avoidTerms ?? [],
+            PreferredTerms = preferredTerms ?? [],
+        },
+        Topics = ["tech"],
+        PersonaDescription = "A tech leader",
+        ExampleContent = [],
+        IsActive = true,
+    };
+
+    private void SetupDbSets(Content[]? contents = null, BrandProfile[]? profiles = null)
+    {
+        var contentMock = (contents ?? []).AsQueryable().BuildMockDbSet();
+        contentMock.Setup(d => d.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
+            .Returns<object[], CancellationToken>((keys, _) =>
+                ValueTask.FromResult((contents ?? []).FirstOrDefault(c => c.Id == (Guid)keys[0])));
+        _dbContext.Setup(d => d.Contents).Returns(contentMock.Object);
+
+        var profileMock = (profiles ?? []).AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(d => d.BrandProfiles).Returns(profileMock.Object);
+    }
+
+    private void SetupSidecarResponse(string jsonResponse)
+    {
+        _sidecar.Setup(s => s.SendTaskAsync(
+                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .Returns(CreateAsyncEnumerable(
+                new ChatEvent("text", jsonResponse, null, null),
+                new TaskCompleteEvent("session-1", 100, 50)));
+    }
+
+    private static async IAsyncEnumerable<SidecarEvent> CreateAsyncEnumerable(
+        params SidecarEvent[] events)
+    {
+        foreach (var e in events)
+        {
+            yield return e;
+            await Task.CompletedTask;
+        }
+    }
+
+    // --- RunRuleChecks ---
+
+    [Fact]
+    public void RunRuleChecks_DetectsAvoidedTerms()
+    {
+        var profile = CreateProfile(avoidTerms: ["leverage", "synergy"]);
+        var sut = CreateSut();
+
+        var result = sut.RunRuleChecks("We leverage synergy to maximize impact", profile);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(2, result.Value!.Count);
+        Assert.Contains(result.Value, v => v.Contains("leverage", StringComparison.OrdinalIgnoreCase));
+        Assert.Contains(result.Value, v => v.Contains("synergy", StringComparison.OrdinalIgnoreCase));
+    }
+
+    [Fact]
+    public void RunRuleChecks_WarnsWhenNoPreferredTermsPresent()
+    {
+        var profile = CreateProfile(preferredTerms: ["AI", "automation", "branding"]);
+        var sut = CreateSut();
+
+        var result = sut.RunRuleChecks("A generic post about nothing specific", profile);
+
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!);
+        Assert.Contains("preferred", result.Value![0], StringComparison.OrdinalIgnoreCase);
+    }
+
+    [Fact]
+    public void RunRuleChecks_ReturnsEmptyForCompliantContent()
+    {
+        var profile = CreateProfile(
+            preferredTerms: ["AI"],
+            avoidTerms: ["leverage"]);
+        var sut = CreateSut();
+
+        var result = sut.RunRuleChecks("AI is transforming the industry", profile);
+
+        Assert.True(result.IsSuccess);
+        Assert.Empty(result.Value!);
+    }
+
+    [Fact]
+    public void RunRuleChecks_IsCaseInsensitive()
+    {
+        var profile = CreateProfile(avoidTerms: ["leverage"]);
+        var sut = CreateSut();
+
+        var result = sut.RunRuleChecks("We LEVERAGE this", profile);
+
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!);
+    }
+
+    [Fact]
+    public void RunRuleChecks_StripsHtmlBeforeChecking()
+    {
+        var profile = CreateProfile(avoidTerms: ["bold"]);
+        var sut = CreateSut();
+
+        var result = sut.RunRuleChecks("<p>This is <b>bold</b> text</p>", profile);
+
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!);
+    }
+
+    // --- ScoreContentAsync ---
+
+    [Fact]
+    public async Task ScoreContentAsync_ParsesJsonResponseIntoBrandVoiceScore()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Great AI content");
+        var profile = CreateProfile();
+        SetupDbSets(contents: [content], profiles: [profile]);
+
+        var json = JsonSerializer.Serialize(new
+        {
+            overallScore = 85,
+            toneAlignment = 90,
+            vocabularyConsistency = 80,
+            personaFidelity = 85,
+            issues = new[] { "Minor tone shift" },
+        });
+        SetupSidecarResponse(json);
+
+        var sut = CreateSut();
+        var result = await sut.ScoreContentAsync(content.Id, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(85, result.Value!.OverallScore);
+        Assert.Equal(90, result.Value.ToneAlignment);
+        Assert.Equal(80, result.Value.VocabularyConsistency);
+        Assert.Equal(85, result.Value.PersonaFidelity);
+    }
+
+    [Fact]
+    public async Task ScoreContentAsync_HandlesInvalidJsonGracefully()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Some content");
+        var profile = CreateProfile();
+        SetupDbSets(contents: [content], profiles: [profile]);
+        SetupSidecarResponse("I think the score is about 7 out of 10");
+
+        var sut = CreateSut();
+        var result = await sut.ScoreContentAsync(content.Id, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task ScoreContentAsync_ReturnsNotFoundWhenContentMissing()
+    {
+        SetupDbSets();
+
+        var sut = CreateSut();
+        var result = await sut.ScoreContentAsync(Guid.NewGuid(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task ScoreContentAsync_StoresScoreInContentMetadata()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Great AI content");
+        var profile = CreateProfile();
+        SetupDbSets(contents: [content], profiles: [profile]);
+
+        var json = JsonSerializer.Serialize(new
+        {
+            overallScore = 85,
+            toneAlignment = 90,
+            vocabularyConsistency = 80,
+            personaFidelity = 85,
+            issues = Array.Empty<string>(),
+        });
+        SetupSidecarResponse(json);
+
+        var sut = CreateSut();
+        await sut.ScoreContentAsync(content.Id, CancellationToken.None);
+
+        Assert.True(content.Metadata.PlatformSpecificData.ContainsKey("BrandVoiceScore"));
+    }
+
+    [Fact]
+    public async Task ScoreContentAsync_SendsPromptToSidecar()
+    {
+        var content = Content.Create(ContentType.SocialPost, "AI content");
+        var profile = CreateProfile();
+        SetupDbSets(contents: [content], profiles: [profile]);
+
+        var json = JsonSerializer.Serialize(new
+        {
+            overallScore = 85, toneAlignment = 90,
+            vocabularyConsistency = 80, personaFidelity = 85,
+            issues = Array.Empty<string>(),
+        });
+        SetupSidecarResponse(json);
+
+        var sut = CreateSut();
+        await sut.ScoreContentAsync(content.Id, CancellationToken.None);
+
+        _sidecar.Verify(s => s.SendTaskAsync(
+            It.Is<string>(p => p.Contains("AI content") && p.Contains("professional")),
+            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    // --- ValidateAndGateAsync ---
+
+    [Fact]
+    public async Task ValidateAndGateAsync_AutonomousAutoRegeneratesBelowThreshold()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Bad content");
+        var profile = CreateProfile();
+        SetupDbSets(contents: [content], profiles: [profile]);
+
+        var lowJson = JsonSerializer.Serialize(new
+        {
+            overallScore = 50, toneAlignment = 50,
+            vocabularyConsistency = 50, personaFidelity = 50,
+            issues = Array.Empty<string>(),
+        });
+        var highJson = JsonSerializer.Serialize(new
+        {
+            overallScore = 80, toneAlignment = 80,
+            vocabularyConsistency = 80, personaFidelity = 80,
+            issues = Array.Empty<string>(),
+        });
+
+        var callCount = 0;
+        _sidecar.Setup(s => s.SendTaskAsync(
+                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .Returns(() =>
+            {
+                callCount++;
+                var json = callCount <= 1 ? lowJson : highJson;
+                return CreateAsyncEnumerable(
+                    new ChatEvent("text", json, null, null),
+                    new TaskCompleteEvent("s1", 100, 50));
+            });
+
+        _pipeline.Setup(p => p.GenerateDraftAsync(content.Id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<string>.Success("Regenerated"));
+
+        var sut = CreateSut();
+        var result = await sut.ValidateAndGateAsync(content.Id, AutonomyLevel.Autonomous, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _pipeline.Verify(p => p.GenerateDraftAsync(content.Id, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task ValidateAndGateAsync_AutonomousFailsAfterMaxAttempts()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Bad content");
+        var profile = CreateProfile();
+        SetupDbSets(contents: [content], profiles: [profile]);
+
+        var lowJson = JsonSerializer.Serialize(new
+        {
+            overallScore = 40, toneAlignment = 40,
+            vocabularyConsistency = 40, personaFidelity = 40,
+            issues = Array.Empty<string>(),
+        });
+        SetupSidecarResponse(lowJson);
+
+        _pipeline.Setup(p => p.GenerateDraftAsync(content.Id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<string>.Success("Regenerated"));
+
+        _options.MaxAutoRegenerateAttempts = 3;
+
+        var sut = CreateSut();
+        var result = await sut.ValidateAndGateAsync(content.Id, AutonomyLevel.Autonomous, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+        _pipeline.Verify(p => p.GenerateDraftAsync(content.Id, It.IsAny<CancellationToken>()), Times.Exactly(3));
+    }
+
+    [Fact]
+    public async Task ValidateAndGateAsync_SemiAutoReturnsAdvisoryScore()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Content");
+        var profile = CreateProfile();
+        SetupDbSets(contents: [content], profiles: [profile]);
+
+        var lowJson = JsonSerializer.Serialize(new
+        {
+            overallScore = 30, toneAlignment = 30,
+            vocabularyConsistency = 30, personaFidelity = 30,
+            issues = Array.Empty<string>(),
+        });
+        SetupSidecarResponse(lowJson);
+
+        var sut = CreateSut();
+        var result = await sut.ValidateAndGateAsync(content.Id, AutonomyLevel.SemiAuto, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _pipeline.Verify(p => p.GenerateDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    [Fact]
+    public async Task ValidateAndGateAsync_ManualReturnsAdvisoryScore()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Content");
+        var profile = CreateProfile();
+        SetupDbSets(contents: [content], profiles: [profile]);
+
+        var lowJson = JsonSerializer.Serialize(new
+        {
+            overallScore = 20, toneAlignment = 20,
+            vocabularyConsistency = 20, personaFidelity = 20,
+            issues = Array.Empty<string>(),
+        });
+        SetupSidecarResponse(lowJson);
+
+        var sut = CreateSut();
+        var result = await sut.ValidateAndGateAsync(content.Id, AutonomyLevel.Manual, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _pipeline.Verify(p => p.GenerateDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+}
