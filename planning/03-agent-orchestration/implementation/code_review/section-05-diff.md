diff --git a/planning/03-agent-orchestration/implementation/deep_implement_config.json b/planning/03-agent-orchestration/implementation/deep_implement_config.json
index 6299ef5..ab7cb25 100644
--- a/planning/03-agent-orchestration/implementation/deep_implement_config.json
+++ b/planning/03-agent-orchestration/implementation/deep_implement_config.json
@@ -19,7 +19,24 @@
     "section-10-api-endpoints",
     "section-11-di-config"
   ],
-  "sections_state": {},
+  "sections_state": {
+    "section-01-domain-entities": {
+      "status": "complete",
+      "commit_hash": "da9ccc9"
+    },
+    "section-02-enums-events": {
+      "status": "complete",
+      "commit_hash": "e24d307"
+    },
+    "section-03-interfaces": {
+      "status": "complete",
+      "commit_hash": "f1f3e2d"
+    },
+    "section-04-ef-core-config": {
+      "status": "complete",
+      "commit_hash": "1080151"
+    }
+  },
   "pre_commit": {
     "present": false,
     "type": "none",
diff --git a/prompts/analytics/performance-insights.liquid b/prompts/analytics/performance-insights.liquid
new file mode 100644
index 0000000..16145fa
--- /dev/null
+++ b/prompts/analytics/performance-insights.liquid
@@ -0,0 +1,18 @@
+Analyze the performance of the following content and provide insights.
+
+{% if task.platform %}Platform: {{ task.platform }}{% endif %}
+{% if task.timeframe %}Timeframe: {{ task.timeframe }}{% endif %}
+{% if task.metrics %}Key metrics: {{ task.metrics }}{% endif %}
+
+{% if content %}
+Content analyzed:
+{{ content.Title }}
+{{ content.Body }}
+{% endif %}
+
+Requirements:
+- Summarize key performance indicators
+- Identify what worked well and what underperformed
+- Compare against typical benchmarks for this content type
+- Provide 3-5 specific, actionable recommendations for improvement
+- Suggest content topics or formats to test based on the data
diff --git a/prompts/analytics/system.liquid b/prompts/analytics/system.liquid
new file mode 100644
index 0000000..d0aa2d8
--- /dev/null
+++ b/prompts/analytics/system.liquid
@@ -0,0 +1,11 @@
+You are a content analytics specialist for a personal brand.
+
+{{ brand_voice_block }}
+
+Your role is to analyze content performance, identify patterns, and provide actionable recommendations to improve the brand's content strategy. Focus on data-driven insights that translate into concrete next steps.
+
+Guidelines:
+- Lead with actionable insights, not just data summaries
+- Compare performance against brand benchmarks
+- Identify content patterns that drive engagement
+- Recommend specific improvements with expected impact
diff --git a/prompts/engagement/response-suggestion.liquid b/prompts/engagement/response-suggestion.liquid
new file mode 100644
index 0000000..10cfd60
--- /dev/null
+++ b/prompts/engagement/response-suggestion.liquid
@@ -0,0 +1,12 @@
+Suggest a response to the following engagement opportunity.
+
+Platform: {{ task.platform }}
+{% if task.context %}Context: {{ task.context }}{% endif %}
+{% if task.original_post %}Original post: {{ task.original_post }}{% endif %}
+{% if task.comment %}Comment to respond to: {{ task.comment }}{% endif %}
+
+Requirements:
+- Provide a thoughtful, on-brand response
+- Add value beyond a simple acknowledgment
+- Keep the response conversational and authentic
+- Suggest 2-3 response options with different tones (casual, professional, insightful)
diff --git a/prompts/engagement/system.liquid b/prompts/engagement/system.liquid
new file mode 100644
index 0000000..3e2adba
--- /dev/null
+++ b/prompts/engagement/system.liquid
@@ -0,0 +1,11 @@
+You are an engagement specialist for a personal brand.
+
+{{ brand_voice_block }}
+
+Your role is to help maintain authentic engagement with the audience. This includes crafting thoughtful responses, identifying trends, and suggesting engagement opportunities that align with the brand's values and expertise.
+
+Guidelines:
+- Prioritize authentic, value-adding responses
+- Match the tone of the conversation context
+- Avoid generic or automated-sounding replies
+- Focus on building genuine relationships with the audience
diff --git a/prompts/engagement/trend-analysis.liquid b/prompts/engagement/trend-analysis.liquid
new file mode 100644
index 0000000..5230f5d
--- /dev/null
+++ b/prompts/engagement/trend-analysis.liquid
@@ -0,0 +1,13 @@
+Analyze current trends relevant to the brand's expertise areas.
+
+{% if task.platform %}Platform: {{ task.platform }}{% endif %}
+{% if task.timeframe %}Timeframe: {{ task.timeframe }}{% endif %}
+{% if task.topics %}Focus areas: {{ task.topics }}{% endif %}
+
+Brand topics of expertise: {{ brand.Topics | join: ", " }}
+
+Requirements:
+- Identify trending topics that intersect with brand expertise
+- Rate each trend's relevance and engagement potential
+- Suggest content angles for the most promising trends
+- Flag any trends to avoid or approach cautiously
diff --git a/prompts/repurpose/blog-to-social.liquid b/prompts/repurpose/blog-to-social.liquid
new file mode 100644
index 0000000..6f1c1e4
--- /dev/null
+++ b/prompts/repurpose/blog-to-social.liquid
@@ -0,0 +1,20 @@
+Repurpose the following blog post into a social media post.
+
+Target platform: {{ task.platform }}
+{% if task.tone %}Tone: {{ task.tone }}{% endif %}
+
+Original blog post:
+{{ content.Title }}
+
+{{ content.Body }}
+
+{% if platforms %}
+Platform constraints:
+- Character limit: {{ platforms.char_limit }}
+{% endif %}
+
+Requirements:
+- Distill the blog post into a single compelling social post
+- Lead with the most surprising or valuable insight
+- Fit within platform character limits
+- Include a call to action to read the full post
diff --git a/prompts/repurpose/blog-to-thread.liquid b/prompts/repurpose/blog-to-thread.liquid
new file mode 100644
index 0000000..3d2bd30
--- /dev/null
+++ b/prompts/repurpose/blog-to-thread.liquid
@@ -0,0 +1,16 @@
+Repurpose the following blog post into a social media thread.
+
+{% if task.platform %}Target platform: {{ task.platform }}{% endif %}
+{% if task.thread_length %}Target thread length: {{ task.thread_length }} posts{% endif %}
+
+Original blog post:
+{{ content.Title }}
+
+{{ content.Body }}
+
+Requirements:
+- Extract the most compelling points from the blog post
+- Create a hook post that drives curiosity
+- Each post should deliver a standalone insight
+- Maintain the original post's key arguments
+- End with a summary and link back to the full post
diff --git a/prompts/repurpose/system.liquid b/prompts/repurpose/system.liquid
new file mode 100644
index 0000000..2c2dee6
--- /dev/null
+++ b/prompts/repurpose/system.liquid
@@ -0,0 +1,11 @@
+You are a content repurposing specialist for a personal brand.
+
+{{ brand_voice_block }}
+
+Your role is to transform existing content into new formats while preserving the core message and brand voice. Adapt the tone, length, and structure to fit the target format and platform.
+
+Guidelines:
+- Preserve the original message's key insights
+- Adapt formatting for the target medium
+- Maintain brand voice consistency across formats
+- Add platform-specific elements where appropriate
diff --git a/prompts/repurpose/thread-to-posts.liquid b/prompts/repurpose/thread-to-posts.liquid
new file mode 100644
index 0000000..07f3688
--- /dev/null
+++ b/prompts/repurpose/thread-to-posts.liquid
@@ -0,0 +1,12 @@
+Repurpose the following thread into individual social media posts for multiple platforms.
+
+{% if task.platforms %}Target platforms: {{ task.platforms }}{% endif %}
+
+Original thread:
+{{ content.Body }}
+
+Requirements:
+- Create one standalone post per target platform
+- Adapt length, tone, and formatting per platform conventions
+- Preserve the core insight from the thread
+- Each post should work independently without thread context
diff --git a/prompts/shared/brand-voice.liquid b/prompts/shared/brand-voice.liquid
new file mode 100644
index 0000000..b425ffc
--- /dev/null
+++ b/prompts/shared/brand-voice.liquid
@@ -0,0 +1,16 @@
+You are writing as {{ brand.Name }}.
+
+Persona: {{ brand.PersonaDescription }}
+
+Tone: {{ brand.ToneDescriptors | join: ", " }}
+
+Style Guidelines: {{ brand.StyleGuidelines }}
+
+{% if brand.PreferredTerms.size > 0 %}
+Preferred vocabulary: {{ brand.PreferredTerms | join: ", " }}
+{% endif %}
+{% if brand.AvoidedTerms.size > 0 %}
+Avoid these terms: {{ brand.AvoidedTerms | join: ", " }}
+{% endif %}
+
+Topics of expertise: {{ brand.Topics | join: ", " }}
diff --git a/prompts/social/post.liquid b/prompts/social/post.liquid
new file mode 100644
index 0000000..8ea27b4
--- /dev/null
+++ b/prompts/social/post.liquid
@@ -0,0 +1,22 @@
+Create a social media post for {{ task.platform }}.
+
+{% if task.topic %}Topic: {{ task.topic }}{% endif %}
+{% if task.tone %}Tone: {{ task.tone }}{% endif %}
+{% if task.hook %}Hook/angle: {{ task.hook }}{% endif %}
+
+{% if platforms %}
+Platform constraints:
+- Character limit: {{ platforms.char_limit }}
+- Hashtag strategy: {{ platforms.hashtag_strategy }}
+{% endif %}
+
+{% if content %}
+Based on this content:
+{{ content.Body }}
+{% endif %}
+
+Requirements:
+- Attention-grabbing opening line
+- Clear value proposition or insight
+- Platform-appropriate formatting and length
+- Natural call to engagement (not forced)
diff --git a/prompts/social/system.liquid b/prompts/social/system.liquid
new file mode 100644
index 0000000..66192f5
--- /dev/null
+++ b/prompts/social/system.liquid
@@ -0,0 +1,11 @@
+You are a social media content creator for a personal brand.
+
+{{ brand_voice_block }}
+
+Your role is to create engaging social media content that drives meaningful interaction. Each post should provide value, spark conversation, or share insights that resonate with the target audience.
+
+Guidelines:
+- Adapt tone and length to the target platform
+- Use platform-native formatting (threads, carousels, etc.)
+- Prioritize authenticity over virality
+- Include clear calls to engagement where appropriate
diff --git a/prompts/social/thread.liquid b/prompts/social/thread.liquid
new file mode 100644
index 0000000..f33c54a
--- /dev/null
+++ b/prompts/social/thread.liquid
@@ -0,0 +1,17 @@
+Create a social media thread on {{ task.platform }}.
+
+Topic: {{ task.topic }}
+{% if task.thread_length %}Target length: {{ task.thread_length }} posts{% endif %}
+
+{% if content %}
+Source material:
+{{ content.Body }}
+{% endif %}
+
+Requirements:
+- Strong hook in the first post that stops the scroll
+- Each post should stand alone while building on the narrative
+- Number posts for clarity (1/, 2/, etc.)
+- Include a takeaway or actionable insight
+- End with a summary post and call to engagement
+- Keep each post within platform character limits
diff --git a/prompts/writer/article.liquid b/prompts/writer/article.liquid
new file mode 100644
index 0000000..4a4ab41
--- /dev/null
+++ b/prompts/writer/article.liquid
@@ -0,0 +1,18 @@
+Write an article on the following topic:
+
+Topic: {{ task.topic }}
+{% if task.publication %}Target publication: {{ task.publication }}{% endif %}
+{% if task.angle %}Angle: {{ task.angle }}{% endif %}
+{% if task.target_length %}Target length: {{ task.target_length }} words{% endif %}
+
+{% if content %}
+Reference material:
+{{ content.Body }}
+{% endif %}
+
+Requirements:
+- Professional tone suitable for the target publication
+- Well-researched and substantiated points
+- Clear thesis statement in the introduction
+- Logical flow of arguments with supporting evidence
+- Polished conclusion that reinforces the key message
diff --git a/prompts/writer/blog-post.liquid b/prompts/writer/blog-post.liquid
new file mode 100644
index 0000000..7ebdca9
--- /dev/null
+++ b/prompts/writer/blog-post.liquid
@@ -0,0 +1,18 @@
+Write a blog post on the following topic:
+
+Topic: {{ task.topic }}
+{% if task.keywords %}Keywords to incorporate: {{ task.keywords }}{% endif %}
+{% if task.target_length %}Target length: {{ task.target_length }} words{% endif %}
+
+{% if content %}
+Context from existing content:
+Title: {{ content.Title }}
+{{ content.Body }}
+{% endif %}
+
+Requirements:
+- Create an engaging headline
+- Write a compelling introduction that hooks the reader
+- Break content into clear sections with subheadings
+- Include practical takeaways
+- End with a strong conclusion and call to action
diff --git a/prompts/writer/system.liquid b/prompts/writer/system.liquid
new file mode 100644
index 0000000..579a166
--- /dev/null
+++ b/prompts/writer/system.liquid
@@ -0,0 +1,11 @@
+You are a professional content writer for a personal brand.
+
+{{ brand_voice_block }}
+
+Your role is to create high-quality, authentic written content that reflects the brand's voice and expertise. Focus on providing genuine value to readers while maintaining consistency with the brand's established tone and style.
+
+Guidelines:
+- Write in the brand's authentic voice
+- Provide actionable insights and real-world examples
+- Maintain professional credibility while being approachable
+- Structure content for readability with clear headings and flow
diff --git a/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj b/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
index 12fa7cf..7d54c78 100644
--- a/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
+++ b/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
@@ -10,6 +10,7 @@
   </ItemGroup>
 
   <ItemGroup>
+    <PackageReference Include="Fluid.Core" Version="2.31.0" />
     <PackageReference Include="Microsoft.AspNetCore.DataProtection" Version="10.0.5" />
     <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.5" />
     <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.5">
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PromptTemplateService.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PromptTemplateService.cs
new file mode 100644
index 0000000..67a3c6e
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/PromptTemplateService.cs
@@ -0,0 +1,131 @@
+using System.Collections.Concurrent;
+using Fluid;
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Infrastructure.Services;
+
+public sealed class PromptTemplateService : IPromptTemplateService, IDisposable
+{
+    private readonly string _promptsPath;
+    private readonly IHostEnvironment _environment;
+    private readonly ILogger<PromptTemplateService> _logger;
+    private readonly ConcurrentDictionary<string, IFluidTemplate> _cache = new();
+    private readonly FluidParser _parser = new();
+    private readonly TemplateOptions _templateOptions;
+    private readonly FileSystemWatcher? _watcher;
+
+    public PromptTemplateService(
+        string promptsPath,
+        IHostEnvironment environment,
+        ILogger<PromptTemplateService> logger)
+    {
+        _promptsPath = promptsPath;
+        _environment = environment;
+        _logger = logger;
+
+        _templateOptions = new TemplateOptions();
+        _templateOptions.MemberAccessStrategy.Register<BrandProfilePromptModel>();
+        _templateOptions.MemberAccessStrategy.Register<ContentPromptModel>();
+
+        if (_environment.IsDevelopment() && Directory.Exists(_promptsPath))
+        {
+            _watcher = new FileSystemWatcher(_promptsPath, "*.liquid")
+            {
+                IncludeSubdirectories = true,
+                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
+            };
+            _watcher.Changed += OnTemplateFileChanged;
+            _watcher.Created += OnTemplateFileChanged;
+            _watcher.Deleted += OnTemplateFileChanged;
+            _watcher.EnableRaisingEvents = true;
+        }
+    }
+
+    public async Task<string> RenderAsync(
+        string agentName,
+        string templateName,
+        Dictionary<string, object> variables)
+    {
+        var cacheKey = $"{agentName}/{templateName}";
+        var template = GetOrParseTemplate(cacheKey);
+
+        var context = new TemplateContext(_templateOptions);
+
+        // Inject brand voice block if shared template exists
+        var brandVoiceKey = "shared/brand-voice";
+        var brandVoicePath = Path.Combine(_promptsPath, "shared", "brand-voice.liquid");
+        if (File.Exists(brandVoicePath) || _cache.ContainsKey(brandVoiceKey))
+        {
+            var brandVoiceTemplate = GetOrParseTemplate(brandVoiceKey);
+            var brandVoiceContext = new TemplateContext(_templateOptions);
+            foreach (var (key, value) in variables)
+            {
+                brandVoiceContext.SetValue(key, value);
+            }
+            var brandVoiceBlock = await brandVoiceTemplate.RenderAsync(brandVoiceContext);
+            context.SetValue("brand_voice_block", brandVoiceBlock);
+        }
+
+        foreach (var (key, value) in variables)
+        {
+            context.SetValue(key, value);
+        }
+
+        var result = await template.RenderAsync(context);
+        return result;
+    }
+
+    public string[] ListTemplates(string agentName)
+    {
+        var agentDir = Path.Combine(_promptsPath, agentName);
+        if (!Directory.Exists(agentDir))
+            return [];
+
+        return Directory.GetFiles(agentDir, "*.liquid")
+            .Select(f => Path.GetFileNameWithoutExtension(f))
+            .Order()
+            .ToArray();
+    }
+
+    private IFluidTemplate GetOrParseTemplate(string cacheKey)
+    {
+        if (_cache.TryGetValue(cacheKey, out var cached))
+        {
+            _logger.LogDebug("Template cache hit: {CacheKey}", cacheKey);
+            return cached;
+        }
+
+        var filePath = Path.Combine(_promptsPath, $"{cacheKey}.liquid");
+        if (!File.Exists(filePath))
+            throw new FileNotFoundException($"Prompt template not found: {filePath}", filePath);
+
+        var content = File.ReadAllText(filePath);
+        if (!_parser.TryParse(content, out var template, out var error))
+            throw new InvalidOperationException($"Failed to parse template '{cacheKey}': {error}");
+
+        _cache.TryAdd(cacheKey, template);
+        _logger.LogDebug("Template parsed and cached: {CacheKey}", cacheKey);
+        return template;
+    }
+
+    private void OnTemplateFileChanged(object sender, FileSystemEventArgs e)
+    {
+        var relativePath = Path.GetRelativePath(_promptsPath, e.FullPath);
+        var cacheKey = relativePath
+            .Replace(Path.DirectorySeparatorChar, '/')
+            .Replace(".liquid", "");
+
+        if (_cache.TryRemove(cacheKey, out _))
+        {
+            _logger.LogInformation("Template cache evicted: {CacheKey}", cacheKey);
+        }
+    }
+
+    public void Dispose()
+    {
+        _watcher?.Dispose();
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/PromptTemplateServiceTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/PromptTemplateServiceTests.cs
new file mode 100644
index 0000000..cc073b8
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/PromptTemplateServiceTests.cs
@@ -0,0 +1,191 @@
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Logging;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services;
+
+public class PromptTemplateServiceTests : IDisposable
+{
+    private readonly string _tempDir;
+    private readonly Mock<IHostEnvironment> _hostEnv;
+    private readonly Mock<ILogger<PromptTemplateService>> _logger;
+
+    public PromptTemplateServiceTests()
+    {
+        _tempDir = Path.Combine(Path.GetTempPath(), $"prompt_tests_{Guid.NewGuid():N}");
+        Directory.CreateDirectory(_tempDir);
+
+        _hostEnv = new Mock<IHostEnvironment>();
+        _hostEnv.Setup(e => e.EnvironmentName).Returns("Production");
+
+        _logger = new Mock<ILogger<PromptTemplateService>>();
+    }
+
+    public void Dispose()
+    {
+        if (Directory.Exists(_tempDir))
+            Directory.Delete(_tempDir, recursive: true);
+    }
+
+    private PromptTemplateService CreateService() =>
+        new(_tempDir, _hostEnv.Object, _logger.Object);
+
+    private void WriteTemplate(string relativePath, string content)
+    {
+        var fullPath = Path.Combine(_tempDir, relativePath);
+        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
+        File.WriteAllText(fullPath, content);
+    }
+
+    [Fact]
+    public async Task RenderAsync_LoadsTemplateFromCorrectPath()
+    {
+        WriteTemplate("writer/blog-post.liquid", "Hello {{ name }}");
+        var service = CreateService();
+
+        var result = await service.RenderAsync("writer", "blog-post",
+            new Dictionary<string, object> { ["name"] = "World" });
+
+        Assert.Equal("Hello World", result);
+    }
+
+    [Fact]
+    public async Task RenderAsync_InjectsBrandVoiceBlock()
+    {
+        WriteTemplate("shared/brand-voice.liquid", "I am {{ brand.Name }}, the brand voice.");
+        WriteTemplate("writer/system.liquid", "Voice: {{ brand_voice_block }}");
+        var service = CreateService();
+
+        var brand = new BrandProfilePromptModel
+        {
+            Name = "TestBrand",
+            PersonaDescription = "A test persona",
+            ToneDescriptors = ["professional"],
+            StyleGuidelines = "Keep it short",
+            PreferredTerms = ["innovation"],
+            AvoidedTerms = ["synergy"],
+            Topics = ["tech"],
+            ExampleContent = ["Example post"]
+        };
+
+        var result = await service.RenderAsync("writer", "system",
+            new Dictionary<string, object> { ["brand"] = brand });
+
+        Assert.Contains("I am TestBrand, the brand voice.", result);
+    }
+
+    [Fact]
+    public async Task RenderAsync_RendersVariablesIntoTemplate()
+    {
+        WriteTemplate("writer/test.liquid", "{{ brand.Name }} writes about {{ brand.Topics | join: ', ' }}");
+        var service = CreateService();
+
+        var brand = new BrandProfilePromptModel
+        {
+            Name = "Matt",
+            PersonaDescription = "AI expert",
+            ToneDescriptors = ["authoritative"],
+            StyleGuidelines = "Direct",
+            PreferredTerms = ["AI"],
+            AvoidedTerms = ["buzzword"],
+            Topics = ["AI", "Leadership"],
+            ExampleContent = []
+        };
+
+        var result = await service.RenderAsync("writer", "test",
+            new Dictionary<string, object> { ["brand"] = brand });
+
+        Assert.Contains("Matt", result);
+        Assert.Contains("AI, Leadership", result);
+    }
+
+    [Fact]
+    public async Task RenderAsync_CachesParsedTemplates_SecondCallDoesNotReReadFile()
+    {
+        WriteTemplate("writer/cached.liquid", "Cached: {{ val }}");
+        var service = CreateService();
+
+        var vars = new Dictionary<string, object> { ["val"] = "first" };
+        var first = await service.RenderAsync("writer", "cached", vars);
+        Assert.Equal("Cached: first", first);
+
+        // Delete the file — cache should still serve the template
+        File.Delete(Path.Combine(_tempDir, "writer/cached.liquid"));
+
+        vars["val"] = "second";
+        var second = await service.RenderAsync("writer", "cached", vars);
+        Assert.Equal("Cached: second", second);
+    }
+
+    [Fact]
+    public async Task RenderAsync_ThrowsWhenTemplateFileNotFound()
+    {
+        var service = CreateService();
+
+        await Assert.ThrowsAsync<FileNotFoundException>(() =>
+            service.RenderAsync("writer", "nonexistent", new Dictionary<string, object>()));
+    }
+
+    [Fact]
+    public void ListTemplates_ReturnsAllLiquidFilesForAgent()
+    {
+        WriteTemplate("writer/system.liquid", "sys");
+        WriteTemplate("writer/blog-post.liquid", "blog");
+        WriteTemplate("writer/article.liquid", "article");
+        var service = CreateService();
+
+        var templates = service.ListTemplates("writer");
+
+        Assert.Equal(3, templates.Length);
+        Assert.Contains("article", templates);
+        Assert.Contains("blog-post", templates);
+        Assert.Contains("system", templates);
+    }
+
+    [Fact]
+    public async Task RenderAsync_UsesPromptViewModelDTOs()
+    {
+        WriteTemplate("writer/full.liquid", "Brand: {{ brand.Name }}, Content: {{ content.Title }}, Type: {{ content.ContentType }}");
+        var service = CreateService();
+
+        var brand = new BrandProfilePromptModel
+        {
+            Name = "TestBrand",
+            PersonaDescription = "desc",
+            ToneDescriptors = ["casual"],
+            StyleGuidelines = "none",
+            PreferredTerms = [],
+            AvoidedTerms = [],
+            Topics = [],
+            ExampleContent = []
+        };
+
+        var content = new ContentPromptModel
+        {
+            Title = "My Post",
+            Body = "Body text",
+            ContentType = ContentType.BlogPost,
+            Status = ContentStatus.Draft,
+            TargetPlatforms = [PlatformType.LinkedIn]
+        };
+
+        var result = await service.RenderAsync("writer", "full",
+            new Dictionary<string, object> { ["brand"] = brand, ["content"] = content });
+
+        Assert.Contains("Brand: TestBrand", result);
+        Assert.Contains("Content: My Post", result);
+    }
+
+    [Fact]
+    public void ListTemplates_ReturnsEmptyForNonexistentAgent()
+    {
+        var service = CreateService();
+
+        var templates = service.ListTemplates("nonexistent");
+
+        Assert.Empty(templates);
+    }
+}
