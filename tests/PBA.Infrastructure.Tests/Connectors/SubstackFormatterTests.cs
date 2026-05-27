using System.Text.Json.Nodes;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;
using PBA.Infrastructure.Connectors;
using Xunit;

namespace PBA.Infrastructure.Tests.Connectors;

public class SubstackFormatterTests
{
    private readonly SubstackFormatter _formatter = new();

    private static PreprocessedContent MakeContent(string body) =>
        new("Test Title", body, null, [], []);

    [Fact]
    public async Task Format_MultipleElements_ProducesValidTiptapDocument()
    {
        var result = await _formatter.FormatAsync(MakeContent("Hello world\n\nSecond paragraph"), default);
        var doc = JsonNode.Parse(result)!;

        Assert.Equal("doc", doc["type"]!.GetValue<string>());
        var content = doc["content"]!.AsArray();
        Assert.Equal(2, content.Count);
        Assert.All(content, node => Assert.Equal("paragraph", node!["type"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Format_Paragraph_MapsToParagraphNode()
    {
        var result = await _formatter.FormatAsync(MakeContent("This is a paragraph."), default);
        var doc = JsonNode.Parse(result)!;
        var content = doc["content"]!.AsArray();

        Assert.Single(content);
        Assert.Equal("paragraph", content[0]!["type"]!.GetValue<string>());
        var textNodes = content[0]!["content"]!.AsArray();
        Assert.Equal("text", textNodes[0]!["type"]!.GetValue<string>());
        Assert.Equal("This is a paragraph.", textNodes[0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Format_Heading_MapsToHeadingNodeWithLevel()
    {
        var result = await _formatter.FormatAsync(MakeContent("## My Heading"), default);
        var doc = JsonNode.Parse(result)!;
        var content = doc["content"]!.AsArray();

        Assert.Equal("heading", content[0]!["type"]!.GetValue<string>());
        Assert.Equal(2, content[0]!["attrs"]!["level"]!.GetValue<int>());
        var textNodes = content[0]!["content"]!.AsArray();
        Assert.Equal("My Heading", textNodes[0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Format_BulletList_MapsToBulletListNode()
    {
        var result = await _formatter.FormatAsync(MakeContent("- Item one\n- Item two\n- Item three"), default);
        var doc = JsonNode.Parse(result)!;
        var content = doc["content"]!.AsArray();

        Assert.Equal("bulletList", content[0]!["type"]!.GetValue<string>());
        var items = content[0]!["content"]!.AsArray();
        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.Equal("listItem", item!["type"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Format_Image_MapsToCaptionedImageNode()
    {
        var result = await _formatter.FormatAsync(MakeContent("![My caption](https://example.com/photo.png)"), default);
        var doc = JsonNode.Parse(result)!;
        var content = doc["content"]!.AsArray();

        Assert.Equal("captionedImage", content[0]!["type"]!.GetValue<string>());
        Assert.Equal("https://example.com/photo.png", content[0]!["attrs"]!["src"]!.GetValue<string>());
        Assert.Equal("My caption", content[0]!["attrs"]!["alt"]!.GetValue<string>());
    }

    [Fact]
    public async Task Format_CodeBlock_MapsToCodeBlockNode()
    {
        var result = await _formatter.FormatAsync(MakeContent("```python\nprint('hello')\n```"), default);
        var doc = JsonNode.Parse(result)!;
        var content = doc["content"]!.AsArray();

        Assert.Equal("codeBlock", content[0]!["type"]!.GetValue<string>());
        var textNodes = content[0]!["content"]!.AsArray();
        Assert.Contains("print('hello')", textNodes[0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Format_BoldText_AddsMarkToTextNode()
    {
        var result = await _formatter.FormatAsync(MakeContent("This is **bold** text."), default);
        var doc = JsonNode.Parse(result)!;
        var textNodes = doc["content"]![0]!["content"]!.AsArray();

        var boldNode = textNodes.FirstOrDefault(n => n!["text"]!.GetValue<string>() == "bold");
        Assert.NotNull(boldNode);
        var marks = boldNode!["marks"]!.AsArray();
        Assert.Contains(marks, m => m!["type"]!.GetValue<string>() == "bold");
    }

    [Fact]
    public async Task Format_ItalicText_AddsMarkToTextNode()
    {
        var result = await _formatter.FormatAsync(MakeContent("This is *italic* text."), default);
        var doc = JsonNode.Parse(result)!;
        var textNodes = doc["content"]![0]!["content"]!.AsArray();

        var italicNode = textNodes.FirstOrDefault(n => n!["text"]!.GetValue<string>() == "italic");
        Assert.NotNull(italicNode);
        var marks = italicNode!["marks"]!.AsArray();
        Assert.Contains(marks, m => m!["type"]!.GetValue<string>() == "italic");
    }

    [Fact]
    public async Task Format_InjectsSubscribeWidget_AfterExecutiveSummary()
    {
        var markdown = "## Executive Summary\n\nSome summary text.\n\n## Next Section\n\nMore content.";
        var result = await _formatter.FormatAsync(MakeContent(markdown), default);
        var doc = JsonNode.Parse(result)!;
        var content = doc["content"]!.AsArray();

        var widgetIndex = -1;
        for (var i = 0; i < content.Count; i++)
        {
            if (content[i]!["type"]!.GetValue<string>() == "subscribeWidget")
            {
                widgetIndex = i;
                break;
            }
        }

        Assert.True(widgetIndex > 0, "subscribeWidget should be present");
        Assert.Equal("paragraph", content[widgetIndex - 1]!["type"]!.GetValue<string>());
        Assert.Equal("heading", content[widgetIndex + 1]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Format_StripsReferencesSection()
    {
        var markdown = "## Main Content\n\nSome text.\n\n## References\n\n- [Link 1](url1)\n- [Link 2](url2)";
        var result = await _formatter.FormatAsync(MakeContent(markdown), default);
        var doc = JsonNode.Parse(result)!;
        var content = doc["content"]!.AsArray();

        foreach (var node in content)
        {
            if (node!["type"]!.GetValue<string>() == "heading" && node["content"] is JsonArray texts)
            {
                var text = texts[0]!["text"]!.GetValue<string>();
                Assert.NotEqual("References", text);
            }
        }
    }

    [Fact]
    public async Task Format_StripsAuthorBio()
    {
        var markdown = "## Main Content\n\nSome text.\n\n## About the Author\n\nSome bio text.";
        var result = await _formatter.FormatAsync(MakeContent(markdown), default);
        var doc = JsonNode.Parse(result)!;
        var content = doc["content"]!.AsArray();

        foreach (var node in content)
        {
            if (node!["type"]!.GetValue<string>() == "heading" && node["content"] is JsonArray texts)
            {
                var text = texts[0]!["text"]!.GetValue<string>();
                Assert.NotEqual("About the Author", text);
            }
        }
    }

    [Fact]
    public void Platform_ReturnsSubstack()
    {
        Assert.Equal(Platform.Substack, _formatter.Platform);
    }
}
