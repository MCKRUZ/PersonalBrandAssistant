using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;

namespace PBA.Infrastructure.Services;

public class AiConnectionsService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISidecarClient _sidecarClient;
    private readonly ILogger<AiConnectionsService> _logger;

    private static readonly TimeSpan AnalysisInterval = TimeSpan.FromHours(6);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AiConnectionsService(
        IServiceScopeFactory scopeFactory,
        ISidecarClient sidecarClient,
        ILogger<AiConnectionsService> logger)
    {
        _scopeFactory = scopeFactory;
        _sidecarClient = sidecarClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AnalyzeConnectionsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "AI connections analysis failed");
            }

            await Task.Delay(AnalysisInterval, stoppingToken);
        }
    }

    internal async Task AnalyzeConnectionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var savedIdeas = await dbContext.Ideas
            .Where(i => i.Status == IdeaStatus.Saved)
            .OrderByDescending(i => i.DetectedAt)
            .Take(50)
            .Select(i => new IdeaSummary(i.Id, i.Title, i.Description, i.Url, i.Tags))
            .ToListAsync(ct);

        if (savedIdeas.Count == 0)
        {
            _logger.LogDebug("No saved ideas to analyze");
            return;
        }

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(savedIdeas);

        var response = await _sidecarClient.SendPromptAsync(systemPrompt, userPrompt, model: null, ct);

        var connections = ParseResponse(response);
        if (connections == null)
            return;

        var allIds = connections
            .SelectMany(c => c.IdeaIds)
            .Where(s => Guid.TryParse(s, out _))
            .Select(Guid.Parse)
            .Distinct()
            .ToList();

        var ideasById = await dbContext.Ideas
            .Where(i => allIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var connectionsByIdea = new Dictionary<Guid, List<AiConnectionGroup>>();
        foreach (var connection in connections)
        {
            foreach (var ideaIdStr in connection.IdeaIds)
            {
                if (!Guid.TryParse(ideaIdStr, out var ideaId) || !ideasById.ContainsKey(ideaId))
                    continue;

                if (!connectionsByIdea.TryGetValue(ideaId, out var list))
                {
                    list = [];
                    connectionsByIdea[ideaId] = list;
                }
                list.Add(connection);
            }
        }

        foreach (var (ideaId, groups) in connectionsByIdea)
        {
            ideasById[ideaId].AIConnections = JsonSerializer.Serialize(groups, JsonOptions);
        }

        await dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("Updated AI connections for {Count} idea groups", connections.Count);
    }

    private static string BuildSystemPrompt() =>
        """
        You are a content strategist for a personal branding expert focused on enterprise AI, digital transformation, and technology leadership. Analyze the following saved content ideas and find thematic connections between them.

        For each group of connected ideas, suggest a specific content angle that combines the ideas into a compelling piece. Think beyond obvious connections -- find non-obvious bridges between topics that would make unique, insightful content.

        Respond with a JSON array (no markdown fences, no extra text):
        [
          {
            "theme": "Short theme label",
            "ideaIds": ["id1", "id2"],
            "suggestedAngle": "Specific content suggestion",
            "confidence": 0.85
          }
        ]

        Rules:
        - Each idea can appear in multiple groups
        - Minimum 2 ideas per group
        - Confidence from 0.0 to 1.0 (how strong the connection is)
        - Maximum 10 groups
        - suggestedAngle should be specific and actionable, not generic
        """;

    private static string BuildUserPrompt(List<IdeaSummary> ideas)
    {
        var lines = new List<string> { "Saved Ideas:", "" };
        for (var i = 0; i < ideas.Count; i++)
        {
            var idea = ideas[i];
            lines.Add($"{i + 1}. [id: {idea.Id}] {idea.Title}");
            if (!string.IsNullOrWhiteSpace(idea.Description))
                lines.Add($"   Description: {idea.Description}");
            if (!string.IsNullOrWhiteSpace(idea.Url))
                lines.Add($"   URL: {idea.Url}");
            if (idea.Tags.Count > 0)
                lines.Add($"   Tags: {string.Join(", ", idea.Tags)}");
            lines.Add("");
        }
        return string.Join('\n', lines);
    }

    private List<AiConnectionGroup>? ParseResponse(string response)
    {
        var json = response.Trim();
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            json = json[(firstNewline + 1)..];
            var lastFence = json.LastIndexOf("```");
            if (lastFence >= 0) json = json[..lastFence];
            json = json.Trim();
        }

        try
        {
            return JsonSerializer.Deserialize<List<AiConnectionGroup>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI connections JSON: {Response}",
                response[..Math.Min(200, response.Length)]);
            return null;
        }
    }

    private record IdeaSummary(Guid Id, string Title, string? Description, string? Url, List<string> Tags);

    internal record AiConnectionGroup(
        string Theme,
        List<string> IdeaIds,
        string SuggestedAngle,
        double Confidence);
}
