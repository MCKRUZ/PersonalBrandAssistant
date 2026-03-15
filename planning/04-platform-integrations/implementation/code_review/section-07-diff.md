diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/InstagramContentFormatter.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/InstagramContentFormatter.cs
new file mode 100644
index 0000000..df27398
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/InstagramContentFormatter.cs
@@ -0,0 +1,63 @@
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;
+
+public sealed class InstagramContentFormatter : IPlatformContentFormatter
+{
+    private const int MaxCaptionLength = 2200;
+    private const int MaxHashtags = 30;
+    private const int MaxCarouselItems = 10;
+
+    public PlatformType Platform => PlatformType.Instagram;
+
+    public Result<PlatformContent> FormatAndValidate(Content content)
+    {
+        if (string.IsNullOrWhiteSpace(content.Body))
+        {
+            return Result.ValidationFailure<PlatformContent>(["Instagram caption cannot be empty"]);
+        }
+
+        if (!HasMedia(content))
+        {
+            return Result.ValidationFailure<PlatformContent>(
+                ["Instagram requires at least one media attachment"]);
+        }
+
+        if (content.Metadata.PlatformSpecificData.TryGetValue("carousel_count", out var carouselStr) &&
+            int.TryParse(carouselStr, out var carouselCount) &&
+            carouselCount > MaxCarouselItems)
+        {
+            return Result.ValidationFailure<PlatformContent>(
+                [$"Instagram carousel is limited to {MaxCarouselItems} items"]);
+        }
+
+        var caption = content.Body.Trim();
+
+        // Build hashtag string (limited to 30)
+        var tags = content.Metadata.Tags
+            .Take(MaxHashtags)
+            .Select(t => t.StartsWith('#') ? t : $"#{t}")
+            .ToList();
+
+        if (tags.Count > 0)
+        {
+            caption = $"{caption}\n\n{string.Join(" ", tags)}";
+        }
+
+        if (caption.Length > MaxCaptionLength)
+        {
+            caption = caption[..(MaxCaptionLength - 3)] + "...";
+        }
+
+        return Result.Success(new PlatformContent(
+            caption, content.Title, content.ContentType,
+            Array.Empty<MediaFile>(), new Dictionary<string, string>()));
+    }
+
+    private static bool HasMedia(Content content) =>
+        content.Metadata.PlatformSpecificData.TryGetValue("media_count", out var mc) &&
+        int.TryParse(mc, out var count) && count > 0;
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/LinkedInContentFormatter.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/LinkedInContentFormatter.cs
new file mode 100644
index 0000000..ca52343
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/LinkedInContentFormatter.cs
@@ -0,0 +1,44 @@
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;
+
+public sealed class LinkedInContentFormatter : IPlatformContentFormatter
+{
+    private const int MaxLength = 3000;
+
+    public PlatformType Platform => PlatformType.LinkedIn;
+
+    public Result<PlatformContent> FormatAndValidate(Content content)
+    {
+        if (string.IsNullOrWhiteSpace(content.Body))
+        {
+            return Result.ValidationFailure<PlatformContent>(["LinkedIn post body cannot be empty"]);
+        }
+
+        var text = content.Body.Trim();
+
+        // Append tags not already present inline
+        var tagsToAppend = content.Metadata.Tags
+            .Where(tag => !text.Contains($"#{tag}", StringComparison.OrdinalIgnoreCase))
+            .Select(tag => tag.StartsWith('#') ? tag : $"#{tag}")
+            .ToList();
+
+        if (tagsToAppend.Count > 0)
+        {
+            text = $"{text}\n\n{string.Join(" ", tagsToAppend)}";
+        }
+
+        if (text.Length > MaxLength)
+        {
+            return Result.ValidationFailure<PlatformContent>(
+                [$"LinkedIn post exceeds {MaxLength} character limit ({text.Length} chars)"]);
+        }
+
+        return Result.Success(new PlatformContent(
+            text, content.Title, content.ContentType,
+            Array.Empty<MediaFile>(), new Dictionary<string, string>()));
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/TwitterContentFormatter.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/TwitterContentFormatter.cs
new file mode 100644
index 0000000..dc5bcea
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/TwitterContentFormatter.cs
@@ -0,0 +1,141 @@
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;
+
+public sealed class TwitterContentFormatter : IPlatformContentFormatter
+{
+    private const int MaxTweetLength = 280;
+
+    public PlatformType Platform => PlatformType.TwitterX;
+
+    public Result<PlatformContent> FormatAndValidate(Content content)
+    {
+        if (string.IsNullOrWhiteSpace(content.Body))
+        {
+            return Result.ValidationFailure<PlatformContent>(["Tweet body cannot be empty"]);
+        }
+
+        var text = content.Body.Trim();
+        var hashtags = FormatHashtags(content.Metadata.Tags);
+
+        if (FitsInSingleTweet(text, hashtags))
+        {
+            var single = AppendHashtags(text, hashtags);
+            return Result.Success(new PlatformContent(
+                single, content.Title, content.ContentType,
+                Array.Empty<MediaFile>(), new Dictionary<string, string>()));
+        }
+
+        return BuildThread(text, hashtags, content);
+    }
+
+    private static bool FitsInSingleTweet(string text, string hashtags)
+    {
+        var total = hashtags.Length > 0 ? text.Length + 1 + hashtags.Length : text.Length;
+        return total <= MaxTweetLength;
+    }
+
+    private static string AppendHashtags(string text, string hashtags)
+    {
+        if (hashtags.Length == 0) return text;
+        var combined = $"{text} {hashtags}";
+        return combined.Length <= MaxTweetLength ? combined : text;
+    }
+
+    private static Result<PlatformContent> BuildThread(
+        string text, string hashtags, Content content)
+    {
+        var sentences = SplitIntoSentences(text);
+        var parts = new List<string>();
+        var current = "";
+
+        foreach (var sentence in sentences)
+        {
+            // Reserve space for " N/N" numbering (estimate max 6 chars)
+            var testLength = current.Length == 0
+                ? sentence.Length + 6
+                : current.Length + 1 + sentence.Length + 6;
+
+            if (testLength <= MaxTweetLength)
+            {
+                current = current.Length == 0 ? sentence : $"{current} {sentence}";
+            }
+            else
+            {
+                if (current.Length > 0) parts.Add(current);
+                current = sentence.Length + 6 <= MaxTweetLength
+                    ? sentence
+                    : Truncate(sentence, MaxTweetLength - 6);
+            }
+        }
+
+        if (current.Length > 0) parts.Add(current);
+
+        if (parts.Count == 0)
+        {
+            parts.Add(Truncate(text, MaxTweetLength - 6));
+        }
+
+        // Try to append hashtags to last part
+        if (hashtags.Length > 0)
+        {
+            var last = parts[^1];
+            var withTags = $"{last} {hashtags}";
+            var numberingSuffix = $" {parts.Count}/{parts.Count}";
+            if (withTags.Length + numberingSuffix.Length <= MaxTweetLength)
+            {
+                parts[^1] = withTags;
+            }
+        }
+
+        // Add numbering
+        var total = parts.Count;
+        for (var i = 0; i < parts.Count; i++)
+        {
+            parts[i] = $"{parts[i]} {i + 1}/{total}";
+        }
+
+        var metadata = new Dictionary<string, string>();
+        for (var i = 1; i < parts.Count; i++)
+        {
+            metadata[$"thread:{i}"] = parts[i];
+        }
+
+        return Result.Success(new PlatformContent(
+            parts[0], content.Title, content.ContentType,
+            Array.Empty<MediaFile>(), metadata));
+    }
+
+    private static List<string> SplitIntoSentences(string text)
+    {
+        var sentences = new List<string>();
+        var start = 0;
+
+        for (var i = 0; i < text.Length; i++)
+        {
+            if ((text[i] == '.' || text[i] == '!' || text[i] == '?') &&
+                (i + 1 >= text.Length || text[i + 1] == ' '))
+            {
+                sentences.Add(text[start..(i + 1)].Trim());
+                start = i + 1;
+            }
+        }
+
+        if (start < text.Length)
+        {
+            var remaining = text[start..].Trim();
+            if (remaining.Length > 0) sentences.Add(remaining);
+        }
+
+        return sentences;
+    }
+
+    private static string Truncate(string text, int maxLength) =>
+        text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
+
+    private static string FormatHashtags(List<string> tags) =>
+        tags.Count == 0 ? "" : string.Join(" ", tags.Select(t => t.StartsWith('#') ? t : $"#{t}"));
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/YouTubeContentFormatter.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/YouTubeContentFormatter.cs
new file mode 100644
index 0000000..f0eb775
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Formatters/YouTubeContentFormatter.cs
@@ -0,0 +1,50 @@
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;
+
+public sealed class YouTubeContentFormatter : IPlatformContentFormatter
+{
+    private const int MaxTitleLength = 100;
+    private const int MaxDescriptionLength = 5000;
+
+    public PlatformType Platform => PlatformType.YouTube;
+
+    public Result<PlatformContent> FormatAndValidate(Content content)
+    {
+        if (string.IsNullOrWhiteSpace(content.Title))
+        {
+            return Result.ValidationFailure<PlatformContent>(["YouTube video requires a title"]);
+        }
+
+        if (string.IsNullOrWhiteSpace(content.Body))
+        {
+            return Result.ValidationFailure<PlatformContent>(["YouTube video requires a description"]);
+        }
+
+        var title = content.Title.Trim();
+        if (title.Length > MaxTitleLength)
+        {
+            title = title[..(MaxTitleLength - 3)] + "...";
+        }
+
+        var description = content.Body.Trim();
+        if (description.Length > MaxDescriptionLength)
+        {
+            description = description[..(MaxDescriptionLength - 3)] + "...";
+        }
+
+        var metadata = new Dictionary<string, string>();
+
+        if (content.Metadata.Tags.Count > 0)
+        {
+            metadata["tags"] = string.Join(",", content.Metadata.Tags);
+        }
+
+        return Result.Success(new PlatformContent(
+            description, title, content.ContentType,
+            Array.Empty<MediaFile>(), metadata));
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/InstagramContentFormatterTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/InstagramContentFormatterTests.cs
new file mode 100644
index 0000000..5e7a618
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/InstagramContentFormatterTests.cs
@@ -0,0 +1,123 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;
+
+public class InstagramContentFormatterTests
+{
+    private readonly InstagramContentFormatter _sut = new();
+
+    [Fact]
+    public void Platform_IsInstagram() =>
+        Assert.Equal(PlatformType.Instagram, _sut.Platform);
+
+    [Fact]
+    public void FormatAndValidate_NoMedia_ReturnsFailure()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Beautiful day!");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public void FormatAndValidate_WithMedia_Succeeds()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Beautiful day!");
+        content.Metadata.PlatformSpecificData["media_count"] = "1";
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("Beautiful day!", result.Value!.Text);
+    }
+
+    [Fact]
+    public void FormatAndValidate_WithMediaCount_Succeeds()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Photo caption");
+        content.Metadata.PlatformSpecificData["media_count"] = "3";
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+    }
+
+    [Fact]
+    public void FormatAndValidate_TruncatesCaptionAt2200()
+    {
+        var body = new string('A', 2500);
+        var content = Content.Create(ContentType.SocialPost, body);
+        content.Metadata.PlatformSpecificData["media_count"] = "1";
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.True(result.Value!.Text.Length <= 2200);
+        Assert.EndsWith("...", result.Value.Text);
+    }
+
+    [Fact]
+    public void FormatAndValidate_LimitsHashtagsTo30()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Post");
+        content.Metadata.PlatformSpecificData["media_count"] = "1";
+        for (var i = 0; i < 35; i++)
+            content.Metadata.Tags.Add($"tag{i}");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        var hashtagCount = result.Value!.Text.Split('#').Length - 1;
+        Assert.True(hashtagCount <= 30);
+    }
+
+    [Fact]
+    public void FormatAndValidate_CarouselOver10_ReturnsFailure()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Carousel post");
+        content.Metadata.PlatformSpecificData["media_count"] = "1";
+        content.Metadata.PlatformSpecificData["carousel_count"] = "12";
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public void FormatAndValidate_CarouselAt10_Succeeds()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Carousel post");
+        content.Metadata.PlatformSpecificData["media_count"] = "10";
+        content.Metadata.PlatformSpecificData["carousel_count"] = "10";
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+    }
+
+    [Fact]
+    public void FormatAndValidate_HashtagsSeparatedByBlankLine()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Nice photo");
+        content.Metadata.PlatformSpecificData["media_count"] = "1";
+        content.Metadata.Tags.AddRange(["travel", "nature"]);
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.Contains("\n\n#travel", result.Value!.Text);
+    }
+
+    [Fact]
+    public void FormatAndValidate_EmptyBodyWithMedia_ReturnsFailure()
+    {
+        var content = Content.Create(ContentType.SocialPost, "");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.False(result.IsSuccess);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LinkedInContentFormatterTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LinkedInContentFormatterTests.cs
new file mode 100644
index 0000000..5dd4f0f
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LinkedInContentFormatterTests.cs
@@ -0,0 +1,108 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;
+
+public class LinkedInContentFormatterTests
+{
+    private readonly LinkedInContentFormatter _sut = new();
+
+    [Fact]
+    public void Platform_IsLinkedIn() =>
+        Assert.Equal(PlatformType.LinkedIn, _sut.Platform);
+
+    [Fact]
+    public void FormatAndValidate_NormalText_Succeeds()
+    {
+        var content = Content.Create(ContentType.BlogPost, "A thoughtful LinkedIn post about leadership.");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.Contains("leadership", result.Value!.Text);
+    }
+
+    [Fact]
+    public void FormatAndValidate_TextAt3000Chars_Succeeds()
+    {
+        var body = new string('A', 3000);
+        var content = Content.Create(ContentType.BlogPost, body);
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+    }
+
+    [Fact]
+    public void FormatAndValidate_TextExceeds3000Chars_ReturnsFailure()
+    {
+        var body = new string('A', 3500);
+        var content = Content.Create(ContentType.BlogPost, body);
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public void FormatAndValidate_PreservesInlineHashtags()
+    {
+        var content = Content.Create(ContentType.BlogPost, "Great insights on #leadership and #innovation today.");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.Contains("#leadership", result.Value!.Text);
+        Assert.Contains("#innovation", result.Value!.Text);
+    }
+
+    [Fact]
+    public void FormatAndValidate_AppendsTagsNotAlreadyInline()
+    {
+        var content = Content.Create(ContentType.BlogPost, "Post about #leadership today.");
+        content.Metadata.Tags.AddRange(["leadership", "tech"]);
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        // "leadership" is already inline, should not be duplicated
+        Assert.Contains("#tech", result.Value!.Text);
+        // Count occurrences of #leadership — should be exactly 1
+        var count = result.Value.Text.Split("#leadership").Length - 1;
+        Assert.Equal(1, count);
+    }
+
+    [Fact]
+    public void FormatAndValidate_TagsPushOver3000_ReturnsFailure()
+    {
+        var body = new string('A', 2995);
+        var content = Content.Create(ContentType.BlogPost, body);
+        content.Metadata.Tags.AddRange(["verylongtag"]);
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public void FormatAndValidate_PreservesTitle()
+    {
+        var content = Content.Create(ContentType.BlogPost, "Body text", title: "My Article");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("My Article", result.Value!.Title);
+    }
+
+    [Fact]
+    public void FormatAndValidate_EmptyBody_ReturnsFailure()
+    {
+        var content = Content.Create(ContentType.BlogPost, "");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.False(result.IsSuccess);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/TwitterContentFormatterTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/TwitterContentFormatterTests.cs
new file mode 100644
index 0000000..78c8267
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/TwitterContentFormatterTests.cs
@@ -0,0 +1,137 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;
+
+public class TwitterContentFormatterTests
+{
+    private readonly TwitterContentFormatter _sut = new();
+
+    [Fact]
+    public void Platform_IsTwitterX() =>
+        Assert.Equal(PlatformType.TwitterX, _sut.Platform);
+
+    [Fact]
+    public void FormatAndValidate_ShortText_ReturnsSingleTweet()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Hello world!");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("Hello world!", result.Value!.Text);
+        Assert.False(result.Value.Metadata.ContainsKey("thread:1"));
+    }
+
+    [Fact]
+    public void FormatAndValidate_TextAt280Chars_NoTruncation()
+    {
+        var body = new string('A', 280);
+        var content = Content.Create(ContentType.SocialPost, body);
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(280, result.Value!.Text.Length);
+    }
+
+    [Fact]
+    public void FormatAndValidate_LongContent_SplitsIntoThread()
+    {
+        var body = string.Join(" ", Enumerable.Range(1, 80).Select(i => $"Sentence number {i}."));
+        var content = Content.Create(ContentType.SocialPost, body);
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.True(result.Value!.Metadata.ContainsKey("thread:1"));
+        Assert.True(result.Value.Text.Length <= 280);
+    }
+
+    [Fact]
+    public void FormatAndValidate_Thread_AddsNumbering()
+    {
+        var body = string.Join(" ", Enumerable.Range(1, 80).Select(i => $"Sentence number {i}."));
+        var content = Content.Create(ContentType.SocialPost, body);
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+
+        // Count total parts
+        var threadCount = result.Value!.Metadata.Keys.Count(k => k.StartsWith("thread:")) + 1;
+        Assert.True(threadCount >= 2);
+
+        // First tweet should end with 1/N
+        Assert.EndsWith($" 1/{threadCount}", result.Value.Text);
+
+        // Second tweet should contain 2/N
+        Assert.EndsWith($" 2/{threadCount}", result.Value.Metadata["thread:1"]);
+    }
+
+    [Fact]
+    public void FormatAndValidate_Thread_EachPartWithinLimit()
+    {
+        var body = string.Join(" ", Enumerable.Range(1, 80).Select(i => $"Sentence number {i}."));
+        var content = Content.Create(ContentType.SocialPost, body);
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.True(result.Value!.Text.Length <= 280);
+
+        foreach (var kv in result.Value.Metadata.Where(k => k.Key.StartsWith("thread:")))
+        {
+            Assert.True(kv.Value.Length <= 280, $"{kv.Key} exceeds 280 chars: {kv.Value.Length}");
+        }
+    }
+
+    [Fact]
+    public void FormatAndValidate_AppendsHashtags()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Short post");
+        content.Metadata.Tags.AddRange(["tech", "ai"]);
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.Contains("#tech", result.Value!.Text);
+        Assert.Contains("#ai", result.Value!.Text);
+        Assert.True(result.Value.Text.Length <= 280);
+    }
+
+    [Fact]
+    public void FormatAndValidate_HashtagsDroppedIfExceedLimit()
+    {
+        var body = new string('A', 270);
+        var content = Content.Create(ContentType.SocialPost, body);
+        content.Metadata.Tags.AddRange(["verylonghashtagthatwontfit"]);
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        // Hashtag won't fit so should not be appended
+        Assert.DoesNotContain("#verylonghashtagthatwontfit", result.Value!.Text);
+    }
+
+    [Fact]
+    public void FormatAndValidate_EmptyBody_ReturnsFailure()
+    {
+        var content = Content.Create(ContentType.SocialPost, "");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public void FormatAndValidate_WhitespaceBody_ReturnsFailure()
+    {
+        var content = Content.Create(ContentType.SocialPost, "   ");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.False(result.IsSuccess);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/YouTubeContentFormatterTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/YouTubeContentFormatterTests.cs
new file mode 100644
index 0000000..6dd3f0e
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/YouTubeContentFormatterTests.cs
@@ -0,0 +1,98 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;
+
+public class YouTubeContentFormatterTests
+{
+    private readonly YouTubeContentFormatter _sut = new();
+
+    [Fact]
+    public void Platform_IsYouTube() =>
+        Assert.Equal(PlatformType.YouTube, _sut.Platform);
+
+    [Fact]
+    public void FormatAndValidate_ValidTitleAndBody_Succeeds()
+    {
+        var content = Content.Create(ContentType.VideoDescription, "Video description here", title: "My Video");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("My Video", result.Value!.Title);
+        Assert.Equal("Video description here", result.Value.Text);
+    }
+
+    [Fact]
+    public void FormatAndValidate_NullTitle_ReturnsFailure()
+    {
+        var content = Content.Create(ContentType.VideoDescription, "Description only");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public void FormatAndValidate_EmptyTitle_ReturnsFailure()
+    {
+        var content = Content.Create(ContentType.VideoDescription, "Description", title: "   ");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public void FormatAndValidate_TitleOver100Chars_Truncates()
+    {
+        var title = new string('T', 150);
+        var content = Content.Create(ContentType.VideoDescription, "Description", title: title);
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.True(result.Value!.Title!.Length <= 100);
+        Assert.EndsWith("...", result.Value.Title);
+    }
+
+    [Fact]
+    public void FormatAndValidate_DescriptionOver5000Chars_Truncates()
+    {
+        var body = new string('D', 6000);
+        var content = Content.Create(ContentType.VideoDescription, body, title: "Title");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.True(result.Value!.Text.Length <= 5000);
+        Assert.EndsWith("...", result.Value.Text);
+    }
+
+    [Fact]
+    public void FormatAndValidate_TagsStoredInMetadata()
+    {
+        var content = Content.Create(ContentType.VideoDescription, "Desc", title: "Title");
+        content.Metadata.Tags.AddRange(["csharp", "dotnet", "tutorial"]);
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.True(result.IsSuccess);
+        Assert.True(result.Value!.Metadata.ContainsKey("tags"));
+        Assert.Contains("csharp", result.Value.Metadata["tags"]);
+        Assert.Contains("dotnet", result.Value.Metadata["tags"]);
+        // Tags should NOT be in the description text
+        Assert.DoesNotContain("#csharp", result.Value.Text);
+    }
+
+    [Fact]
+    public void FormatAndValidate_EmptyBody_ReturnsFailure()
+    {
+        var content = Content.Create(ContentType.VideoDescription, "", title: "Title");
+
+        var result = _sut.FormatAndValidate(content);
+
+        Assert.False(result.IsSuccess);
+    }
+}
