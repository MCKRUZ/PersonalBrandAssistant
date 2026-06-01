diff --git a/src/PBA.Infrastructure/Configuration/SubstackOptions.cs b/src/PBA.Infrastructure/Configuration/SubstackOptions.cs
new file mode 100644
index 0000000..8cf979a
--- /dev/null
+++ b/src/PBA.Infrastructure/Configuration/SubstackOptions.cs
@@ -0,0 +1,10 @@
+namespace PBA.Infrastructure.Configuration;
+
+public sealed class SubstackOptions
+{
+    public const string SectionName = "Publishing:Substack";
+
+    public bool Enabled { get; init; }
+    public string PublicationSlug { get; init; } = string.Empty;
+    public string DefaultAudience { get; init; } = "everyone";
+}
diff --git a/src/PBA.Infrastructure/Connectors/SubstackConnector.cs b/src/PBA.Infrastructure/Connectors/SubstackConnector.cs
new file mode 100644
index 0000000..a6afe4e
--- /dev/null
+++ b/src/PBA.Infrastructure/Connectors/SubstackConnector.cs
@@ -0,0 +1,264 @@
+using System.Net;
+using System.Text;
+using System.Text.Json;
+using System.Text.Json.Nodes;
+using System.Text.Json.Serialization;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+
+namespace PBA.Infrastructure.Connectors;
+
+public sealed class SubstackConnector(
+    HttpClient httpClient,
+    IAppDbContext db,
+    ITokenEncryptor encryptor,
+    IOptionsMonitor<SubstackOptions> options,
+    ILogger<SubstackConnector> logger) : IPlatformConnector
+{
+    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
+    {
+        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
+        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
+    };
+
+    public Platform Platform => Platform.Substack;
+
+    public async Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)
+    {
+        try
+        {
+            if (!options.CurrentValue.Enabled)
+                return new PlatformPublishResult(false, null, null,
+                    "Substack publishing is disabled. Enable it in configuration.");
+
+            var credential = await GetActiveCredentialAsync(ct);
+            var cookies = GetCookies(credential);
+            if (cookies is null)
+                return new PlatformPublishResult(false, null, null,
+                    "Substack session cookies not found. Please log in via Settings.");
+
+            var bylineId = await GetBylineIdAsync(cookies, ct);
+            if (bylineId is null)
+                return new PlatformPublishResult(false, null, null,
+                    "Substack session expired. Please re-login in Settings.");
+
+            var draftId = await CreateDraftAsync(request, bylineId.Value, cookies, ct);
+            if (draftId is null)
+                return new PlatformPublishResult(false, null, null,
+                    "Failed to create Substack draft.");
+
+            if (request.Tags.Count > 0)
+                await AddTagsAsync(draftId, request.Tags, cookies, ct);
+
+            if (request.Mode is PublishMode.Draft or PublishMode.Schedule)
+                return new PlatformPublishResult(true, null, draftId, null);
+
+            var publishedUrl = await PublishDraftAsync(draftId, cookies, ct);
+            if (publishedUrl is null)
+                return new PlatformPublishResult(false, null, draftId,
+                    "Draft created but publish failed. Check Substack dashboard.");
+
+            return new PlatformPublishResult(true, publishedUrl, draftId, null);
+        }
+        catch (InvalidOperationException ex)
+        {
+            logger.LogError(ex, "Failed to publish to Substack");
+            return new PlatformPublishResult(false, null, null, ex.Message);
+        }
+        catch (Exception ex)
+        {
+            logger.LogError(ex, "Failed to publish to Substack");
+            return new PlatformPublishResult(false, null, null,
+                "An unexpected error occurred while publishing to Substack.");
+        }
+    }
+
+    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct)
+    {
+        try
+        {
+            var credential = await GetActiveCredentialAsync(ct);
+            var cookies = GetCookies(credential);
+            if (cookies is null) return false;
+
+            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
+            AttachCookies(request, cookies);
+
+            var response = await httpClient.SendAsync(request, ct);
+            return response.StatusCode == HttpStatusCode.OK;
+        }
+        catch (Exception ex)
+        {
+            logger.LogWarning(ex, "Substack credential validation failed");
+            return false;
+        }
+    }
+
+    public PlatformCapabilities GetCapabilities() => new(
+        MaxCharacters: int.MaxValue,
+        SupportsMarkdown: false,
+        SupportsHtml: false,
+        SupportsImages: true,
+        SupportsScheduling: false,
+        SupportsThreads: false,
+        SupportedMediaTypes: ["image/png", "image/jpeg", "image/gif", "image/webp"]
+    );
+
+    private async Task<int?> GetBylineIdAsync(
+        Dictionary<string, string> cookies, CancellationToken ct)
+    {
+        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
+        AttachCookies(request, cookies);
+
+        var response = await httpClient.SendAsync(request, ct);
+        var body = await response.Content.ReadAsStringAsync(ct);
+        logger.LogDebug("Substack GET /api/v1/me ({StatusCode}): {Body}",
+            response.StatusCode, body);
+
+        if (!response.IsSuccessStatusCode)
+            return null;
+
+        var user = JsonSerializer.Deserialize<SubstackUser>(body, JsonOptions);
+        return user?.BylineId;
+    }
+
+    private async Task<string?> CreateDraftAsync(
+        PlatformPublishRequest publishRequest, int bylineId,
+        Dictionary<string, string> cookies, CancellationToken ct)
+    {
+        var tiptapBody = JsonNode.Parse(publishRequest.TransformedContent);
+
+        var payload = new JsonObject
+        {
+            ["draft_title"] = publishRequest.Content.Title,
+            ["draft_subtitle"] = "",
+            ["draft_body"] = tiptapBody,
+            ["draft_bylines"] = new JsonArray { new JsonObject { ["id"] = bylineId } },
+            ["type"] = "newsletter"
+        };
+
+        var json = payload.ToJsonString();
+        logger.LogDebug("Substack POST /api/v1/drafts request: {Payload}", json);
+
+        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/drafts")
+        {
+            Content = new StringContent(json, Encoding.UTF8, "application/json")
+        };
+        AttachCookies(request, cookies);
+
+        var response = await httpClient.SendAsync(request, ct);
+        var body = await response.Content.ReadAsStringAsync(ct);
+        logger.LogDebug("Substack POST /api/v1/drafts ({StatusCode}): {Body}",
+            response.StatusCode, body);
+
+        if (!response.IsSuccessStatusCode)
+            return null;
+
+        var draft = JsonSerializer.Deserialize<SubstackDraftResponse>(body, JsonOptions);
+        return draft?.Id.ToString();
+    }
+
+    private async Task AddTagsAsync(
+        string draftId, IReadOnlyList<string> tags,
+        Dictionary<string, string> cookies, CancellationToken ct)
+    {
+        var tagsArray = new JsonArray();
+        foreach (var tag in tags)
+            tagsArray.Add(JsonValue.Create(tag));
+
+        var payload = new JsonObject { ["tags"] = tagsArray }.ToJsonString();
+
+        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/post/{draftId}/tags")
+        {
+            Content = new StringContent(payload, Encoding.UTF8, "application/json")
+        };
+        AttachCookies(request, cookies);
+
+        var response = await httpClient.SendAsync(request, ct);
+        var body = await response.Content.ReadAsStringAsync(ct);
+        logger.LogDebug("Substack PUT /api/v1/post/{DraftId}/tags ({StatusCode}): {Body}",
+            draftId, response.StatusCode, body);
+    }
+
+    private async Task<string?> PublishDraftAsync(
+        string draftId, Dictionary<string, string> cookies, CancellationToken ct)
+    {
+        using var prepubRequest = new HttpRequestMessage(
+            HttpMethod.Post, $"/api/v1/drafts/{draftId}/prepublish");
+        AttachCookies(prepubRequest, cookies);
+
+        var prepubResponse = await httpClient.SendAsync(prepubRequest, ct);
+        var prepubBody = await prepubResponse.Content.ReadAsStringAsync(ct);
+        logger.LogDebug("Substack POST prepublish ({StatusCode}): {Body}",
+            prepubResponse.StatusCode, prepubBody);
+
+        if (!prepubResponse.IsSuccessStatusCode)
+            return null;
+
+        var audience = options.CurrentValue.DefaultAudience;
+        var publishPayload = new JsonObject
+        {
+            ["send_email"] = true,
+            ["audience"] = audience
+        }.ToJsonString();
+
+        using var publishRequest = new HttpRequestMessage(
+            HttpMethod.Post, $"/api/v1/drafts/{draftId}/publish")
+        {
+            Content = new StringContent(publishPayload, Encoding.UTF8, "application/json")
+        };
+        AttachCookies(publishRequest, cookies);
+
+        var publishResponse = await httpClient.SendAsync(publishRequest, ct);
+        var publishBody = await publishResponse.Content.ReadAsStringAsync(ct);
+        logger.LogDebug("Substack POST publish ({StatusCode}): {Body}",
+            publishResponse.StatusCode, publishBody);
+
+        if (!publishResponse.IsSuccessStatusCode)
+            return null;
+
+        var result = JsonSerializer.Deserialize<SubstackPublishResponse>(publishBody, JsonOptions);
+        return result?.CanonicalUrl;
+    }
+
+    private Dictionary<string, string>? GetCookies(PlatformCredential credential)
+    {
+        if (string.IsNullOrEmpty(credential.EncryptedCookies)) return null;
+
+        try
+        {
+            var json = encryptor.Decrypt(credential.EncryptedCookies);
+            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
+        }
+        catch (Exception ex)
+        {
+            logger.LogWarning(ex, "Failed to decrypt Substack cookies");
+            return null;
+        }
+    }
+
+    private static void AttachCookies(
+        HttpRequestMessage request, Dictionary<string, string> cookies)
+    {
+        var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
+        request.Headers.Add("Cookie", cookieHeader);
+    }
+
+    private async Task<PlatformCredential> GetActiveCredentialAsync(CancellationToken ct)
+    {
+        return await db.PlatformCredentials
+            .FirstOrDefaultAsync(c => c.Platform == Platform.Substack && c.IsActive, ct)
+            ?? throw new InvalidOperationException(
+                "No active Substack credential found. Connect Substack in Settings.");
+    }
+
+    internal record SubstackUser(int Id, string? Name, string? Email, int BylineId);
+    internal record SubstackDraftResponse(int Id, string? Slug, string? Title);
+    internal record SubstackPublishResponse(int Id, string? Slug, string? CanonicalUrl);
+}
diff --git a/src/PBA.Infrastructure/Connectors/SubstackFormatter.cs b/src/PBA.Infrastructure/Connectors/SubstackFormatter.cs
new file mode 100644
index 0000000..234cf82
--- /dev/null
+++ b/src/PBA.Infrastructure/Connectors/SubstackFormatter.cs
@@ -0,0 +1,346 @@
+using System.Text;
+using System.Text.Json.Nodes;
+using Markdig;
+using Markdig.Syntax;
+using Markdig.Syntax.Inlines;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+
+namespace PBA.Infrastructure.Connectors;
+
+public sealed class SubstackFormatter : IPlatformFormatter
+{
+    private static readonly string[] StrippableHeadings =
+        ["references", "works cited", "about the author", "author bio"];
+
+    public Platform Platform => Platform.Substack;
+
+    public Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct)
+    {
+        var document = Markdown.Parse(content.Body);
+        var blocks = StripTrailingSections(document);
+        var tiptapContent = ConvertBlocks(blocks);
+        InjectSubscribeWidget(tiptapContent);
+
+        var doc = new JsonObject
+        {
+            ["type"] = "doc",
+            ["content"] = tiptapContent
+        };
+
+        return Task.FromResult(doc.ToJsonString());
+    }
+
+    private static List<Block> StripTrailingSections(MarkdownDocument document)
+    {
+        var blocks = document.ToList();
+
+        for (var i = blocks.Count - 1; i >= 0; i--)
+        {
+            if (blocks[i] is HeadingBlock heading)
+            {
+                var text = GetHeadingText(heading);
+                if (StrippableHeadings.Contains(text.ToLowerInvariant()))
+                    blocks.RemoveRange(i, blocks.Count - i);
+            }
+        }
+
+        return blocks;
+    }
+
+    private static string GetHeadingText(HeadingBlock heading)
+    {
+        if (heading.Inline is null) return string.Empty;
+        var sb = new StringBuilder();
+        foreach (var inline in heading.Inline)
+        {
+            if (inline is LiteralInline literal)
+                sb.Append(literal.Content);
+        }
+        return sb.ToString();
+    }
+
+    private static void InjectSubscribeWidget(JsonArray content)
+    {
+        var insertIndex = -1;
+        var foundExecSummary = false;
+
+        for (var i = 0; i < content.Count; i++)
+        {
+            var nodeType = content[i]?["type"]?.GetValue<string>();
+
+            if (nodeType != "heading") continue;
+
+            var text = content[i]?["content"]?[0]?["text"]?.GetValue<string>();
+
+            if (!foundExecSummary &&
+                text?.Equals("Executive Summary", StringComparison.OrdinalIgnoreCase) == true)
+            {
+                foundExecSummary = true;
+                continue;
+            }
+
+            if (foundExecSummary)
+            {
+                insertIndex = i;
+                break;
+            }
+        }
+
+        if (insertIndex > 0)
+            content.Insert(insertIndex, new JsonObject { ["type"] = "subscribeWidget" });
+    }
+
+    private static JsonArray ConvertBlocks(IEnumerable<Block> blocks)
+    {
+        var result = new JsonArray();
+        foreach (var block in blocks)
+        {
+            var node = ConvertBlock(block);
+            if (node is not null)
+                result.Add(node);
+        }
+        return result;
+    }
+
+    private static JsonNode? ConvertBlock(Block block) => block switch
+    {
+        HeadingBlock heading => ConvertHeading(heading),
+        ParagraphBlock paragraph => ConvertParagraph(paragraph),
+        ListBlock list => ConvertList(list),
+        FencedCodeBlock code => ConvertFencedCodeBlock(code),
+        CodeBlock code => ConvertCodeBlock(code),
+        QuoteBlock quote => ConvertQuote(quote),
+        ThematicBreakBlock => new JsonObject { ["type"] = "horizontalRule" },
+        ListItemBlock item => ConvertListItem(item),
+        _ => null
+    };
+
+    private static JsonObject ConvertHeading(HeadingBlock heading)
+    {
+        var node = new JsonObject
+        {
+            ["type"] = "heading",
+            ["attrs"] = new JsonObject { ["level"] = heading.Level }
+        };
+
+        if (heading.Inline is not null)
+        {
+            var content = ConvertInlines(heading.Inline);
+            if (content.Count > 0)
+                node["content"] = content;
+        }
+
+        return node;
+    }
+
+    private static JsonNode? ConvertParagraph(ParagraphBlock paragraph)
+    {
+        if (paragraph.Inline is null) return null;
+
+        LinkInline? standaloneImage = null;
+        var hasTextContent = false;
+
+        foreach (var inline in paragraph.Inline)
+        {
+            if (inline is LinkInline { IsImage: true } img)
+                standaloneImage = img;
+            else if (inline is LiteralInline lit && !string.IsNullOrWhiteSpace(lit.Content.ToString()))
+                hasTextContent = true;
+            else if (inline is not LineBreakInline)
+                hasTextContent = true;
+        }
+
+        if (standaloneImage is not null && !hasTextContent)
+            return CreateCaptionedImage(standaloneImage);
+
+        var content = ConvertInlines(paragraph.Inline);
+        if (content.Count == 0) return null;
+
+        return new JsonObject
+        {
+            ["type"] = "paragraph",
+            ["content"] = content
+        };
+    }
+
+    private static JsonObject CreateCaptionedImage(LinkInline imageLink)
+    {
+        var attrs = new JsonObject { ["src"] = imageLink.Url };
+
+        if (imageLink.FirstChild is LiteralInline altLiteral)
+        {
+            var altText = altLiteral.Content.ToString();
+            if (!string.IsNullOrEmpty(altText))
+                attrs["alt"] = altText;
+        }
+
+        return new JsonObject
+        {
+            ["type"] = "captionedImage",
+            ["attrs"] = attrs
+        };
+    }
+
+    private static JsonObject ConvertList(ListBlock list)
+    {
+        var type = list.IsOrdered ? "orderedList" : "bulletList";
+        var items = new JsonArray();
+
+        foreach (var item in list)
+        {
+            var converted = ConvertBlock(item);
+            if (converted is not null)
+                items.Add(converted);
+        }
+
+        return new JsonObject
+        {
+            ["type"] = type,
+            ["content"] = items
+        };
+    }
+
+    private static JsonObject ConvertListItem(ListItemBlock item)
+    {
+        var content = new JsonArray();
+        foreach (var child in item)
+        {
+            var converted = ConvertBlock(child);
+            if (converted is not null)
+                content.Add(converted);
+        }
+
+        return new JsonObject
+        {
+            ["type"] = "listItem",
+            ["content"] = content
+        };
+    }
+
+    private static JsonObject ConvertFencedCodeBlock(FencedCodeBlock codeBlock)
+    {
+        var node = new JsonObject { ["type"] = "codeBlock" };
+
+        if (!string.IsNullOrEmpty(codeBlock.Info))
+            node["attrs"] = new JsonObject { ["language"] = codeBlock.Info };
+
+        var code = codeBlock.Lines.ToString().TrimEnd('\n', '\r');
+        if (!string.IsNullOrEmpty(code))
+        {
+            node["content"] = new JsonArray
+            {
+                new JsonObject { ["type"] = "text", ["text"] = code }
+            };
+        }
+
+        return node;
+    }
+
+    private static JsonObject ConvertCodeBlock(CodeBlock codeBlock)
+    {
+        var node = new JsonObject { ["type"] = "codeBlock" };
+        var code = codeBlock.Lines.ToString().TrimEnd('\n', '\r');
+
+        if (!string.IsNullOrEmpty(code))
+        {
+            node["content"] = new JsonArray
+            {
+                new JsonObject { ["type"] = "text", ["text"] = code }
+            };
+        }
+
+        return node;
+    }
+
+    private static JsonObject ConvertQuote(QuoteBlock quote)
+    {
+        var content = new JsonArray();
+        foreach (var child in quote)
+        {
+            var converted = ConvertBlock(child);
+            if (converted is not null)
+                content.Add(converted);
+        }
+
+        return new JsonObject
+        {
+            ["type"] = "blockquote",
+            ["content"] = content
+        };
+    }
+
+    private static JsonArray ConvertInlines(ContainerInline container)
+    {
+        var nodes = new JsonArray();
+        CollectInlineNodes(container, nodes, []);
+        return nodes;
+    }
+
+    private static void CollectInlineNodes(
+        Inline inline, JsonArray nodes, List<JsonObject> activeMarks)
+    {
+        switch (inline)
+        {
+            case LiteralInline literal:
+                var text = literal.Content.ToString();
+                if (!string.IsNullOrEmpty(text))
+                    nodes.Add(CreateTextNode(text, activeMarks));
+                break;
+
+            case LineBreakInline { IsHard: true }:
+                nodes.Add(new JsonObject { ["type"] = "hardBreak" });
+                break;
+
+            case LineBreakInline:
+                break;
+
+            case EmphasisInline emphasis:
+                var markType = emphasis.DelimiterCount >= 2 ? "bold" : "italic";
+                activeMarks.Add(new JsonObject { ["type"] = markType });
+                foreach (var child in emphasis)
+                    CollectInlineNodes(child, nodes, activeMarks);
+                activeMarks.RemoveAt(activeMarks.Count - 1);
+                break;
+
+            case CodeInline code:
+                var codeMark = new JsonObject { ["type"] = "code" };
+                nodes.Add(CreateTextNode(code.Content, [.. activeMarks, codeMark]));
+                break;
+
+            case LinkInline { IsImage: true }:
+                break;
+
+            case LinkInline link:
+                var linkMark = new JsonObject
+                {
+                    ["type"] = "link",
+                    ["attrs"] = new JsonObject { ["href"] = link.Url }
+                };
+                activeMarks.Add(linkMark);
+                foreach (var child in link)
+                    CollectInlineNodes(child, nodes, activeMarks);
+                activeMarks.RemoveAt(activeMarks.Count - 1);
+                break;
+
+            case ContainerInline container:
+                foreach (var child in container)
+                    CollectInlineNodes(child, nodes, activeMarks);
+                break;
+        }
+    }
+
+    private static JsonObject CreateTextNode(string text, IReadOnlyList<JsonObject> marks)
+    {
+        var node = new JsonObject { ["type"] = "text", ["text"] = text };
+        if (marks.Count > 0)
+        {
+            var marksArray = new JsonArray();
+            foreach (var mark in marks)
+                marksArray.Add(mark.DeepClone());
+            node["marks"] = marksArray;
+        }
+        return node;
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Connectors/SubstackConnectorTests.cs b/tests/PBA.Infrastructure.Tests/Connectors/SubstackConnectorTests.cs
new file mode 100644
index 0000000..9992f50
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Connectors/SubstackConnectorTests.cs
@@ -0,0 +1,278 @@
+using System.Net;
+using System.Text;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Options;
+using Moq;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Connectors;
+using PBA.Infrastructure.Data;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Connectors;
+
+public class SubstackConnectorTests : IDisposable
+{
+    private readonly ApplicationDbContext _dbContext;
+    private readonly Mock<ITokenEncryptor> _encryptor = new();
+    private readonly MockSubstackHandler _handler = new();
+
+    private const string CookieJson =
+        "{\"substack.sid\":\"sid123\",\"sid\":\"s123\",\"substack.lli\":\"lli123\"}";
+
+    public SubstackConnectorTests()
+    {
+        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(Guid.NewGuid().ToString())
+            .Options;
+        _dbContext = new ApplicationDbContext(dbOptions);
+
+        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>()))
+            .Returns<string>(s => s.Replace("encrypted:", ""));
+    }
+
+    private SubstackConnector CreateConnector(bool enabled = true)
+    {
+        var httpClient = new HttpClient(_handler)
+        {
+            BaseAddress = new Uri("https://matthewkruczek.substack.com")
+        };
+
+        var opts = Mock.Of<IOptionsMonitor<SubstackOptions>>(o =>
+            o.CurrentValue == new SubstackOptions
+            {
+                Enabled = enabled,
+                PublicationSlug = "matthewkruczek",
+                DefaultAudience = "everyone"
+            });
+
+        return new SubstackConnector(
+            httpClient, _dbContext, _encryptor.Object, opts,
+            NullLogger<SubstackConnector>.Instance);
+    }
+
+    private void SeedCredential(string? encryptedCookies = "encrypted:" + CookieJson)
+    {
+        _dbContext.PlatformCredentials.Add(new PlatformCredential
+        {
+            Id = Guid.NewGuid(),
+            Platform = Platform.Substack,
+            EncryptedAccessToken = "",
+            EncryptedCookies = encryptedCookies,
+            IsActive = true
+        });
+        _dbContext.SaveChanges();
+    }
+
+    private static PlatformPublishRequest MakeRequest(
+        PublishMode mode = PublishMode.Draft,
+        IReadOnlyList<string>? tags = null) =>
+        new(
+            new Content { Title = "Test Post" },
+            "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Hello\"}]}]}",
+            tags ?? [],
+            null,
+            mode,
+            null);
+
+    private void SetupDraftFlow()
+    {
+        _handler.Setup(HttpMethod.Get, "/api/v1/me", HttpStatusCode.OK,
+            "{\"id\":1,\"name\":\"Test\",\"byline_id\":789}");
+        _handler.Setup(HttpMethod.Post, "/api/v1/drafts", HttpStatusCode.OK,
+            "{\"id\":12345,\"slug\":\"test-post\",\"title\":\"Test Post\"}");
+    }
+
+    [Fact]
+    public async Task PublishAsync_Draft_CreatesDraftOnly()
+    {
+        SeedCredential();
+        SetupDraftFlow();
+        var connector = CreateConnector();
+
+        var result = await connector.PublishAsync(MakeRequest(PublishMode.Draft), default);
+
+        Assert.True(result.Success);
+        Assert.Equal("12345", result.PlatformPostId);
+        Assert.DoesNotContain(_handler.Requests, r => r.Path.EndsWith("/prepublish"));
+        Assert.DoesNotContain(_handler.Requests, r => r.Path.EndsWith("/publish"));
+    }
+
+    [Fact]
+    public async Task PublishAsync_Publish_ExecutesFullFlow()
+    {
+        SeedCredential();
+        SetupDraftFlow();
+        _handler.Setup(HttpMethod.Post, "/api/v1/drafts/12345/prepublish", HttpStatusCode.OK, "{}");
+        _handler.Setup(HttpMethod.Post, "/api/v1/drafts/12345/publish", HttpStatusCode.OK,
+            "{\"id\":12345,\"slug\":\"test-post\",\"canonical_url\":\"https://matthewkruczek.substack.com/p/test-post\"}");
+        var connector = CreateConnector();
+
+        var result = await connector.PublishAsync(MakeRequest(PublishMode.Publish), default);
+
+        Assert.True(result.Success);
+        Assert.Equal("https://matthewkruczek.substack.com/p/test-post", result.PublishedUrl);
+        Assert.Equal("12345", result.PlatformPostId);
+    }
+
+    [Fact]
+    public async Task PublishAsync_WithTags_CallsTagsEndpoint()
+    {
+        SeedCredential();
+        SetupDraftFlow();
+        _handler.Setup(HttpMethod.Put, "/api/v1/post/12345/tags", HttpStatusCode.OK, "{}");
+        var connector = CreateConnector();
+
+        await connector.PublishAsync(MakeRequest(tags: ["AI", "Engineering"]), default);
+
+        Assert.Contains(_handler.Requests, r =>
+            r.Method == HttpMethod.Put && r.Path == "/api/v1/post/12345/tags");
+    }
+
+    [Fact]
+    public async Task PublishAsync_AttachesCookiesToAllRequests()
+    {
+        SeedCredential();
+        SetupDraftFlow();
+        var connector = CreateConnector();
+
+        await connector.PublishAsync(MakeRequest(), default);
+
+        Assert.All(_handler.Requests, r =>
+        {
+            Assert.NotNull(r.CookieHeader);
+            Assert.Contains("substack.sid=sid123", r.CookieHeader);
+        });
+    }
+
+    [Fact]
+    public async Task PublishAsync_AuthExpired_ReturnsFailure()
+    {
+        SeedCredential();
+        _handler.Setup(HttpMethod.Get, "/api/v1/me", HttpStatusCode.Unauthorized,
+            "{\"error\":true,\"message\":\"Not authorized\"}");
+        var connector = CreateConnector();
+
+        var result = await connector.PublishAsync(MakeRequest(), default);
+
+        Assert.False(result.Success);
+        Assert.Contains("session", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
+    }
+
+    [Fact]
+    public async Task PublishAsync_Disabled_ReturnsFailure()
+    {
+        var connector = CreateConnector(enabled: false);
+
+        var result = await connector.PublishAsync(MakeRequest(), default);
+
+        Assert.False(result.Success);
+        Assert.Contains("disabled", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
+        Assert.Empty(_handler.Requests);
+    }
+
+    [Fact]
+    public async Task PublishAsync_NoCredential_ReturnsFailure()
+    {
+        var connector = CreateConnector();
+
+        var result = await connector.PublishAsync(MakeRequest(), default);
+
+        Assert.False(result.Success);
+        Assert.Contains("credential", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
+    }
+
+    [Fact]
+    public async Task PublishAsync_Schedule_TreatedAsDraft()
+    {
+        SeedCredential();
+        SetupDraftFlow();
+        var connector = CreateConnector();
+
+        var result = await connector.PublishAsync(MakeRequest(PublishMode.Schedule), default);
+
+        Assert.True(result.Success);
+        Assert.DoesNotContain(_handler.Requests, r => r.Path.EndsWith("/publish"));
+    }
+
+    [Fact]
+    public async Task ValidateCredentialsAsync_ValidCookies_ReturnsTrue()
+    {
+        SeedCredential();
+        _handler.Setup(HttpMethod.Get, "/api/v1/me", HttpStatusCode.OK,
+            "{\"id\":1,\"name\":\"Test\",\"byline_id\":789}");
+        var connector = CreateConnector();
+
+        var result = await connector.ValidateCredentialsAsync(default);
+
+        Assert.True(result);
+    }
+
+    [Fact]
+    public async Task ValidateCredentialsAsync_ExpiredCookies_ReturnsFalse()
+    {
+        SeedCredential();
+        _handler.Setup(HttpMethod.Get, "/api/v1/me", HttpStatusCode.Unauthorized,
+            "{\"error\":true,\"message\":\"Not authorized\"}");
+        var connector = CreateConnector();
+
+        var result = await connector.ValidateCredentialsAsync(default);
+
+        Assert.False(result);
+    }
+
+    [Fact]
+    public async Task GetCapabilities_ReturnsCorrectValues()
+    {
+        var connector = CreateConnector();
+        var caps = connector.GetCapabilities();
+
+        Assert.Equal(int.MaxValue, caps.MaxCharacters);
+        Assert.False(caps.SupportsMarkdown);
+        Assert.False(caps.SupportsHtml);
+        Assert.True(caps.SupportsImages);
+        Assert.False(caps.SupportsScheduling);
+        Assert.False(caps.SupportsThreads);
+    }
+
+    public void Dispose() => _dbContext.Dispose();
+
+    private sealed class MockSubstackHandler : HttpMessageHandler
+    {
+        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new();
+        public List<(HttpMethod Method, string Path, string? Body, string? CookieHeader)> Requests { get; } = [];
+
+        public void Setup(HttpMethod method, string path, HttpStatusCode status, string body)
+        {
+            _responses[$"{method}:{path}"] = (status, body);
+        }
+
+        protected override async Task<HttpResponseMessage> SendAsync(
+            HttpRequestMessage request, CancellationToken ct)
+        {
+            var path = request.RequestUri!.AbsolutePath;
+            var body = request.Content is not null
+                ? await request.Content.ReadAsStringAsync(ct)
+                : null;
+            var cookies = request.Headers.Contains("Cookie")
+                ? request.Headers.GetValues("Cookie").FirstOrDefault()
+                : null;
+            Requests.Add((request.Method, path, body, cookies));
+
+            var key = $"{request.Method}:{path}";
+            if (_responses.TryGetValue(key, out var response))
+            {
+                return new HttpResponseMessage(response.Status)
+                {
+                    Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
+                };
+            }
+
+            return new HttpResponseMessage(HttpStatusCode.NotFound);
+        }
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Connectors/SubstackFormatterTests.cs b/tests/PBA.Infrastructure.Tests/Connectors/SubstackFormatterTests.cs
new file mode 100644
index 0000000..fb620c3
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Connectors/SubstackFormatterTests.cs
@@ -0,0 +1,182 @@
+using System.Text.Json.Nodes;
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Connectors;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Connectors;
+
+public class SubstackFormatterTests
+{
+    private readonly SubstackFormatter _formatter = new();
+
+    private static PreprocessedContent MakeContent(string body) =>
+        new("Test Title", body, null, [], []);
+
+    [Fact]
+    public async Task Format_MultipleElements_ProducesValidTiptapDocument()
+    {
+        var result = await _formatter.FormatAsync(MakeContent("Hello world\n\nSecond paragraph"), default);
+        var doc = JsonNode.Parse(result)!;
+
+        Assert.Equal("doc", doc["type"]!.GetValue<string>());
+        var content = doc["content"]!.AsArray();
+        Assert.Equal(2, content.Count);
+        Assert.All(content, node => Assert.Equal("paragraph", node!["type"]!.GetValue<string>()));
+    }
+
+    [Fact]
+    public async Task Format_Paragraph_MapsToParagraphNode()
+    {
+        var result = await _formatter.FormatAsync(MakeContent("This is a paragraph."), default);
+        var doc = JsonNode.Parse(result)!;
+        var content = doc["content"]!.AsArray();
+
+        Assert.Single(content);
+        Assert.Equal("paragraph", content[0]!["type"]!.GetValue<string>());
+        var textNodes = content[0]!["content"]!.AsArray();
+        Assert.Equal("text", textNodes[0]!["type"]!.GetValue<string>());
+        Assert.Equal("This is a paragraph.", textNodes[0]!["text"]!.GetValue<string>());
+    }
+
+    [Fact]
+    public async Task Format_Heading_MapsToHeadingNodeWithLevel()
+    {
+        var result = await _formatter.FormatAsync(MakeContent("## My Heading"), default);
+        var doc = JsonNode.Parse(result)!;
+        var content = doc["content"]!.AsArray();
+
+        Assert.Equal("heading", content[0]!["type"]!.GetValue<string>());
+        Assert.Equal(2, content[0]!["attrs"]!["level"]!.GetValue<int>());
+        var textNodes = content[0]!["content"]!.AsArray();
+        Assert.Equal("My Heading", textNodes[0]!["text"]!.GetValue<string>());
+    }
+
+    [Fact]
+    public async Task Format_BulletList_MapsToBulletListNode()
+    {
+        var result = await _formatter.FormatAsync(MakeContent("- Item one\n- Item two\n- Item three"), default);
+        var doc = JsonNode.Parse(result)!;
+        var content = doc["content"]!.AsArray();
+
+        Assert.Equal("bulletList", content[0]!["type"]!.GetValue<string>());
+        var items = content[0]!["content"]!.AsArray();
+        Assert.Equal(3, items.Count);
+        Assert.All(items, item => Assert.Equal("listItem", item!["type"]!.GetValue<string>()));
+    }
+
+    [Fact]
+    public async Task Format_Image_MapsToCaptionedImageNode()
+    {
+        var result = await _formatter.FormatAsync(MakeContent("![My caption](https://example.com/photo.png)"), default);
+        var doc = JsonNode.Parse(result)!;
+        var content = doc["content"]!.AsArray();
+
+        Assert.Equal("captionedImage", content[0]!["type"]!.GetValue<string>());
+        Assert.Equal("https://example.com/photo.png", content[0]!["attrs"]!["src"]!.GetValue<string>());
+        Assert.Equal("My caption", content[0]!["attrs"]!["alt"]!.GetValue<string>());
+    }
+
+    [Fact]
+    public async Task Format_CodeBlock_MapsToCodeBlockNode()
+    {
+        var result = await _formatter.FormatAsync(MakeContent("```python\nprint('hello')\n```"), default);
+        var doc = JsonNode.Parse(result)!;
+        var content = doc["content"]!.AsArray();
+
+        Assert.Equal("codeBlock", content[0]!["type"]!.GetValue<string>());
+        var textNodes = content[0]!["content"]!.AsArray();
+        Assert.Contains("print('hello')", textNodes[0]!["text"]!.GetValue<string>());
+    }
+
+    [Fact]
+    public async Task Format_BoldText_AddsMarkToTextNode()
+    {
+        var result = await _formatter.FormatAsync(MakeContent("This is **bold** text."), default);
+        var doc = JsonNode.Parse(result)!;
+        var textNodes = doc["content"]![0]!["content"]!.AsArray();
+
+        var boldNode = textNodes.FirstOrDefault(n => n!["text"]!.GetValue<string>() == "bold");
+        Assert.NotNull(boldNode);
+        var marks = boldNode!["marks"]!.AsArray();
+        Assert.Contains(marks, m => m!["type"]!.GetValue<string>() == "bold");
+    }
+
+    [Fact]
+    public async Task Format_ItalicText_AddsMarkToTextNode()
+    {
+        var result = await _formatter.FormatAsync(MakeContent("This is *italic* text."), default);
+        var doc = JsonNode.Parse(result)!;
+        var textNodes = doc["content"]![0]!["content"]!.AsArray();
+
+        var italicNode = textNodes.FirstOrDefault(n => n!["text"]!.GetValue<string>() == "italic");
+        Assert.NotNull(italicNode);
+        var marks = italicNode!["marks"]!.AsArray();
+        Assert.Contains(marks, m => m!["type"]!.GetValue<string>() == "italic");
+    }
+
+    [Fact]
+    public async Task Format_InjectsSubscribeWidget_AfterExecutiveSummary()
+    {
+        var markdown = "## Executive Summary\n\nSome summary text.\n\n## Next Section\n\nMore content.";
+        var result = await _formatter.FormatAsync(MakeContent(markdown), default);
+        var doc = JsonNode.Parse(result)!;
+        var content = doc["content"]!.AsArray();
+
+        var widgetIndex = -1;
+        for (var i = 0; i < content.Count; i++)
+        {
+            if (content[i]!["type"]!.GetValue<string>() == "subscribeWidget")
+            {
+                widgetIndex = i;
+                break;
+            }
+        }
+
+        Assert.True(widgetIndex > 0, "subscribeWidget should be present");
+        Assert.Equal("paragraph", content[widgetIndex - 1]!["type"]!.GetValue<string>());
+        Assert.Equal("heading", content[widgetIndex + 1]!["type"]!.GetValue<string>());
+    }
+
+    [Fact]
+    public async Task Format_StripsReferencesSection()
+    {
+        var markdown = "## Main Content\n\nSome text.\n\n## References\n\n- [Link 1](url1)\n- [Link 2](url2)";
+        var result = await _formatter.FormatAsync(MakeContent(markdown), default);
+        var doc = JsonNode.Parse(result)!;
+        var content = doc["content"]!.AsArray();
+
+        foreach (var node in content)
+        {
+            if (node!["type"]!.GetValue<string>() == "heading" && node["content"] is JsonArray texts)
+            {
+                var text = texts[0]!["text"]!.GetValue<string>();
+                Assert.NotEqual("References", text);
+            }
+        }
+    }
+
+    [Fact]
+    public async Task Format_StripsAuthorBio()
+    {
+        var markdown = "## Main Content\n\nSome text.\n\n## About the Author\n\nSome bio text.";
+        var result = await _formatter.FormatAsync(MakeContent(markdown), default);
+        var doc = JsonNode.Parse(result)!;
+        var content = doc["content"]!.AsArray();
+
+        foreach (var node in content)
+        {
+            if (node!["type"]!.GetValue<string>() == "heading" && node["content"] is JsonArray texts)
+            {
+                var text = texts[0]!["text"]!.GetValue<string>();
+                Assert.NotEqual("About the Author", text);
+            }
+        }
+    }
+
+    [Fact]
+    public void Platform_ReturnsSubstack()
+    {
+        Assert.Equal(Platform.Substack, _formatter.Platform);
+    }
+}
