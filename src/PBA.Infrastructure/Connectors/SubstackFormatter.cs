using System.Text;
using System.Text.Json.Nodes;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Connectors;

public sealed class SubstackFormatter : IPlatformFormatter
{
    private static readonly string[] StrippableHeadings =
        ["references", "works cited", "about the author", "author bio"];

    public Platform Platform => Platform.Substack;

    public Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct)
    {
        var document = Markdown.Parse(content.Body);
        var blocks = StripTrailingSections(document);
        var tiptapContent = ConvertBlocks(blocks);
        InjectSubscribeWidget(tiptapContent);

        var doc = new JsonObject
        {
            ["type"] = "doc",
            ["content"] = tiptapContent
        };

        return Task.FromResult(doc.ToJsonString());
    }

    private static List<Block> StripTrailingSections(MarkdownDocument document)
    {
        var blocks = document.ToList();

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is HeadingBlock heading &&
                StrippableHeadings.Contains(GetHeadingText(heading).ToLowerInvariant()))
            {
                blocks.RemoveRange(i, blocks.Count - i);
                break;
            }
        }

        return blocks;
    }

    private static string GetHeadingText(HeadingBlock heading)
    {
        if (heading.Inline is null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var inline in heading.Inline)
        {
            if (inline is LiteralInline literal)
                sb.Append(literal.Content);
        }
        return sb.ToString();
    }

    private static void InjectSubscribeWidget(JsonArray content)
    {
        var insertIndex = -1;
        var foundExecSummary = false;

        for (var i = 0; i < content.Count; i++)
        {
            var nodeType = content[i]?["type"]?.GetValue<string>();

            if (nodeType != "heading") continue;

            var text = content[i]?["content"]?[0]?["text"]?.GetValue<string>();

            if (!foundExecSummary &&
                text?.Equals("Executive Summary", StringComparison.OrdinalIgnoreCase) == true)
            {
                foundExecSummary = true;
                continue;
            }

            if (foundExecSummary)
            {
                insertIndex = i;
                break;
            }
        }

        if (insertIndex > 0)
            content.Insert(insertIndex, new JsonObject { ["type"] = "subscribeWidget" });
    }

    private static JsonArray ConvertBlocks(IEnumerable<Block> blocks)
    {
        var result = new JsonArray();
        foreach (var block in blocks)
        {
            var node = ConvertBlock(block);
            if (node is not null)
                result.Add(node);
        }
        return result;
    }

    private static JsonNode? ConvertBlock(Block block) => block switch
    {
        HeadingBlock heading => ConvertHeading(heading),
        ParagraphBlock paragraph => ConvertParagraph(paragraph),
        ListBlock list => ConvertList(list),
        FencedCodeBlock code => ConvertFencedCodeBlock(code),
        CodeBlock code => ConvertCodeBlock(code),
        QuoteBlock quote => ConvertQuote(quote),
        ThematicBreakBlock => new JsonObject { ["type"] = "horizontalRule" },
        ListItemBlock item => ConvertListItem(item),
        _ => null
    };

    private static JsonObject ConvertHeading(HeadingBlock heading)
    {
        var node = new JsonObject
        {
            ["type"] = "heading",
            ["attrs"] = new JsonObject { ["level"] = heading.Level }
        };

        if (heading.Inline is not null)
        {
            var content = ConvertInlines(heading.Inline);
            if (content.Count > 0)
                node["content"] = content;
        }

        return node;
    }

    private static JsonNode? ConvertParagraph(ParagraphBlock paragraph)
    {
        if (paragraph.Inline is null) return null;

        LinkInline? standaloneImage = null;
        var hasTextContent = false;

        foreach (var inline in paragraph.Inline)
        {
            if (inline is LinkInline { IsImage: true } img)
                standaloneImage = img;
            else if (inline is LiteralInline lit && !string.IsNullOrWhiteSpace(lit.Content.ToString()))
                hasTextContent = true;
            else if (inline is not LineBreakInline)
                hasTextContent = true;
        }

        if (standaloneImage is not null && !hasTextContent)
            return CreateCaptionedImage(standaloneImage);

        var content = ConvertInlines(paragraph.Inline);
        if (content.Count == 0) return null;

        return new JsonObject
        {
            ["type"] = "paragraph",
            ["content"] = content
        };
    }

    private static JsonObject CreateCaptionedImage(LinkInline imageLink)
    {
        var attrs = new JsonObject { ["src"] = imageLink.Url };

        if (imageLink.FirstChild is LiteralInline altLiteral)
        {
            var altText = altLiteral.Content.ToString();
            if (!string.IsNullOrEmpty(altText))
                attrs["alt"] = altText;
        }

        return new JsonObject
        {
            ["type"] = "captionedImage",
            ["attrs"] = attrs
        };
    }

    private static JsonObject ConvertList(ListBlock list)
    {
        var type = list.IsOrdered ? "orderedList" : "bulletList";
        var items = new JsonArray();

        foreach (var item in list)
        {
            var converted = ConvertBlock(item);
            if (converted is not null)
                items.Add(converted);
        }

        return new JsonObject
        {
            ["type"] = type,
            ["content"] = items
        };
    }

    private static JsonObject ConvertListItem(ListItemBlock item)
    {
        var content = new JsonArray();
        foreach (var child in item)
        {
            var converted = ConvertBlock(child);
            if (converted is not null)
                content.Add(converted);
        }

        return new JsonObject
        {
            ["type"] = "listItem",
            ["content"] = content
        };
    }

    private static JsonObject ConvertFencedCodeBlock(FencedCodeBlock codeBlock)
    {
        var node = new JsonObject { ["type"] = "codeBlock" };

        if (!string.IsNullOrEmpty(codeBlock.Info))
            node["attrs"] = new JsonObject { ["language"] = codeBlock.Info };

        var code = codeBlock.Lines.ToString().TrimEnd('\n', '\r');
        if (!string.IsNullOrEmpty(code))
        {
            node["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = code }
            };
        }

        return node;
    }

    private static JsonObject ConvertCodeBlock(CodeBlock codeBlock)
    {
        var node = new JsonObject { ["type"] = "codeBlock" };
        var code = codeBlock.Lines.ToString().TrimEnd('\n', '\r');

        if (!string.IsNullOrEmpty(code))
        {
            node["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = code }
            };
        }

        return node;
    }

    private static JsonObject ConvertQuote(QuoteBlock quote)
    {
        var content = new JsonArray();
        foreach (var child in quote)
        {
            var converted = ConvertBlock(child);
            if (converted is not null)
                content.Add(converted);
        }

        return new JsonObject
        {
            ["type"] = "blockquote",
            ["content"] = content
        };
    }

    private static JsonArray ConvertInlines(ContainerInline container)
    {
        var nodes = new JsonArray();
        CollectInlineNodes(container, nodes, []);
        return nodes;
    }

    private static void CollectInlineNodes(
        Inline inline, JsonArray nodes, List<JsonObject> activeMarks)
    {
        switch (inline)
        {
            case LiteralInline literal:
                var text = literal.Content.ToString();
                if (!string.IsNullOrEmpty(text))
                    nodes.Add(CreateTextNode(text, activeMarks));
                break;

            case LineBreakInline { IsHard: true }:
                nodes.Add(new JsonObject { ["type"] = "hardBreak" });
                break;

            case LineBreakInline:
                break;

            case EmphasisInline emphasis:
                var markType = emphasis.DelimiterCount >= 2 ? "bold" : "italic";
                activeMarks.Add(new JsonObject { ["type"] = markType });
                foreach (var child in emphasis)
                    CollectInlineNodes(child, nodes, activeMarks);
                activeMarks.RemoveAt(activeMarks.Count - 1);
                break;

            case CodeInline code:
                var codeMark = new JsonObject { ["type"] = "code" };
                nodes.Add(CreateTextNode(code.Content, [.. activeMarks, codeMark]));
                break;

            case LinkInline { IsImage: true }:
                break;

            case LinkInline link:
                var linkMark = new JsonObject
                {
                    ["type"] = "link",
                    ["attrs"] = new JsonObject { ["href"] = link.Url }
                };
                activeMarks.Add(linkMark);
                foreach (var child in link)
                    CollectInlineNodes(child, nodes, activeMarks);
                activeMarks.RemoveAt(activeMarks.Count - 1);
                break;

            case ContainerInline container:
                foreach (var child in container)
                    CollectInlineNodes(child, nodes, activeMarks);
                break;
        }
    }

    private static JsonObject CreateTextNode(string text, IReadOnlyList<JsonObject> marks)
    {
        var node = new JsonObject { ["type"] = "text", ["text"] = text };
        if (marks.Count > 0)
        {
            var marksArray = new JsonArray();
            foreach (var mark in marks)
                marksArray.Add(mark.DeepClone());
            node["marks"] = marksArray;
        }
        return node;
    }
}
