using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;

namespace PBA.Api.Hubs;

public class ContentHub(
    IAppDbContext db,
    ISidecarClient sidecarClient,
    ILogger<ContentHub> logger) : Hub<IContentHubClient>
{
    public async Task SendChatMessage(Guid contentId, string message)
    {
        if (contentId == Guid.Empty || string.IsNullOrWhiteSpace(message))
        {
            await Clients.Caller.GenerationError("Invalid request");
            return;
        }

        var ct = Context.ConnectionAborted;
        var content = await db.Contents.FindAsync([contentId], ct);
        if (content is null)
        {
            await Clients.Caller.GenerationError("Content not found");
            return;
        }

        var profile = await db.BrandProfiles.FirstOrDefaultAsync(ct);

        var systemPrompt = BuildSystemPrompt(profile);
        var userPrompt = BuildUserPrompt(content.Body, message);

        try
        {
            var fullText = new StringBuilder();
            await foreach (var token in sidecarClient.StreamPromptAsync(
                contentId, systemPrompt, userPrompt, ct))
            {
                await Clients.Caller.ReceiveToken(token);
                fullText.Append(token);
            }

            await Clients.Caller.GenerationComplete(fullText.ToString());
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Streaming cancelled for content {ContentId}", contentId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Streaming failed for content {ContentId}", contentId);
            await Clients.Caller.GenerationError("An error occurred during generation");
        }
    }

    private static string BuildSystemPrompt(Domain.Entities.BrandProfile? profile)
    {
        if (profile is null)
            return "You are a helpful content writing assistant.";

        var sb = new StringBuilder();
        sb.AppendLine("You are a content writing assistant. Write in the author's voice.");
        if (!string.IsNullOrWhiteSpace(profile.Personality))
            sb.AppendLine($"Personality: {profile.Personality}");
        if (!string.IsNullOrWhiteSpace(profile.Tone))
            sb.AppendLine($"Tone: {profile.Tone}");
        if (profile.Vocabulary is { Count: > 0 })
            sb.AppendLine($"Preferred vocabulary: {string.Join(", ", profile.Vocabulary)}");
        if (profile.AvoidWords is { Count: > 0 })
            sb.AppendLine($"Avoid these words: {string.Join(", ", profile.AvoidWords)}");
        return sb.ToString();
    }

    private static string BuildUserPrompt(string contentBody, string message)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(contentBody))
        {
            sb.AppendLine("Current content:");
            sb.AppendLine(contentBody);
            sb.AppendLine();
        }
        sb.AppendLine(message);
        return sb.ToString();
    }
}
