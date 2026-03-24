using System.Text;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentAutomation;

public sealed class ImagePromptService : IImagePromptService
{
    private const string SystemPrompt = """
        You are an AI image prompt engineer specializing in FLUX text-to-image generation.

        Given a blog post or social media content, generate a FLUX-compatible image prompt
        that produces a professional, LinkedIn-appropriate visual.

        Visual direction:
        - Clean minimalist compositions with clear focal points
        - Muted corporate palettes (slate blues, teals, warm amber accents)
        - Editorial magazine quality, professional corporate imagery
        - Gradient backgrounds, geometric patterns, abstract tech concepts

        Include style keywords where appropriate: minimalist, flat design, editorial style,
        gradient background, high contrast, professional corporate, isometric, modern.

        AVOID:
        - Text, words, or letters in the image
        - Busy or cluttered compositions
        - Photorealistic human faces
        - Neon or over-saturated colors
        - Stock photo cliches

        Keep the prompt under 200 words. Return ONLY the image prompt text.
        No explanations, no markdown, no preamble.
        """;

    private readonly ISidecarClient _sidecarClient;
    private readonly ILogger<ImagePromptService> _logger;

    public ImagePromptService(ISidecarClient sidecarClient, ILogger<ImagePromptService> logger)
    {
        _sidecarClient = sidecarClient;
        _logger = logger;
    }

    public async Task<string> GeneratePromptAsync(string postContent, CancellationToken ct)
    {
        var task = $"Generate a FLUX image prompt for the following content:\n\n{postContent}";
        var sb = new StringBuilder();

        await foreach (var evt in _sidecarClient.SendTaskAsync(task, SystemPrompt, null, ct))
        {
            switch (evt)
            {
                case ChatEvent { Text: not null } chat:
                    sb.Append(chat.Text);
                    break;
                case ErrorEvent error:
                    _logger.LogError("Sidecar error during image prompt generation: {Message}", error.Message);
                    throw new InvalidOperationException($"Sidecar error: {error.Message}");
                case TaskCompleteEvent:
                    break;
            }
        }

        var result = sb.ToString().Trim();
        if (string.IsNullOrEmpty(result))
        {
            throw new InvalidOperationException("Sidecar returned empty image prompt");
        }

        _logger.LogInformation("Generated image prompt ({Length} chars)", result.Length);
        return result;
    }
}
