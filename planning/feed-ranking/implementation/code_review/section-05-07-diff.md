diff --git a/src/PBA.Api/appsettings.json b/src/PBA.Api/appsettings.json
index 9cf52a9..83e48b4 100644
--- a/src/PBA.Api/appsettings.json
+++ b/src/PBA.Api/appsettings.json
@@ -58,15 +58,12 @@
     "IntervalMinutes": 10,
     "BatchSize": 20,
     "ThrottleMs": 1000,
-    "Model": "google/gemini-2.5-flash",
-    "BackfillEnabled": false
+    "Model": "google/gemini-2.5-flash"
   },
   "Clustering": {
     "IntervalMinutes": 30,
-    "MinScore": 6,
     "LookbackHours": 48,
-    "MaxItemsPerSweep": 40,
-    "Model": "google/gemini-2.5-flash"
+    "MaxItemsPerSweep": 40
   },
   "Digest": {
     "RunAtLocalTime": "07:00",
diff --git a/src/PBA.Application/Common/BrandFit.cs b/src/PBA.Application/Common/BrandFit.cs
new file mode 100644
index 0000000..fb382d6
--- /dev/null
+++ b/src/PBA.Application/Common/BrandFit.cs
@@ -0,0 +1,47 @@
+namespace PBA.Application.Common;
+
+/// <summary>
+/// The single, shared brand-fit formulas. Both the embedding service (section-06) and the scoring sweep
+/// (section-07) call <see cref="EmbeddingWeightedSum"/> so the pre-filter math exists in exactly one
+/// place; the scoring sweep and read-time ranking (section-08) call <see cref="RenormalizedSubScore"/>
+/// for the query-time weighted brandFit. All in-memory; uses <see cref="VectorMath.CosineSimilarity"/>.
+/// </summary>
+public static class BrandFit
+{
+    /// <summary>
+    /// Embedding pre-filter: weighted Σ over pillars of <c>weight × cosine(itemVec, pillarVec)</c>.
+    /// Pillars whose embedding is null/empty are skipped (they contribute nothing and never NaN the sum).
+    /// Not renormalized — it is a coarse gate, not the final brandFit.
+    /// </summary>
+    public static double EmbeddingWeightedSum(
+        float[] itemVec, IReadOnlyList<(double Weight, float[]? Embedding)> pillars)
+    {
+        double sum = 0;
+        foreach (var (weight, embedding) in pillars)
+        {
+            if (embedding is null || embedding.Length == 0) continue;
+            sum += weight * VectorMath.CosineSimilarity(itemVec, embedding);
+        }
+        return sum;
+    }
+
+    /// <summary>
+    /// Query-time brandFit: weight-renormalized average of per-pillar sub-scores over the pillars that
+    /// actually have a sub-score (Σ weight×score / Σ weight). Renormalizing over present pillars keeps
+    /// the value on a 0..1 scale regardless of how many pillars were scored. Returns 0 when no pillar in
+    /// <paramref name="pillars"/> has a sub-score.
+    /// </summary>
+    public static double RenormalizedSubScore(
+        IReadOnlyDictionary<Guid, double> subScoresByPillarId,
+        IReadOnlyList<(Guid Id, double Weight)> pillars)
+    {
+        double weighted = 0, totalWeight = 0;
+        foreach (var (id, weight) in pillars)
+        {
+            if (!subScoresByPillarId.TryGetValue(id, out var score)) continue;
+            weighted += weight * score;
+            totalWeight += weight;
+        }
+        return totalWeight > 0 ? weighted / totalWeight : 0;
+    }
+}
diff --git a/src/PBA.Application/Common/Interfaces/IIdeaAnalyzer.cs b/src/PBA.Application/Common/Interfaces/IIdeaAnalyzer.cs
index ee2b391..e485425 100644
--- a/src/PBA.Application/Common/Interfaces/IIdeaAnalyzer.cs
+++ b/src/PBA.Application/Common/Interfaces/IIdeaAnalyzer.cs
@@ -5,13 +5,11 @@ namespace PBA.Application.Common.Interfaces;
 public interface IIdeaAnalyzer
 {
     /// <summary>
-    /// Scores a single idea for brand content-worthiness (0-10) and returns an
-    /// AI summary, category, and tags. Returns null if the model output cannot be parsed.
+    /// Scores an item against the active profile snapshot. Returns per-pillar sub-scores (0..1 on the
+    /// {0, .25, .5, .75, 1} scale, keyed by <see cref="PillarScore.PillarId"/>), the anti-topic /
+    /// authority flags, and a one-line reason. Returns null on LLM/parse failure, on the central-collapse
+    /// guard (all pillar scores identical, R-M5), or when no returned pillar name maps to the snapshot.
     /// </summary>
     Task<IdeaAnalysis?> AnalyzeAsync(
-        string title,
-        string? description,
-        string? url,
-        string sourceName,
-        CancellationToken ct = default);
+        IdeaAnalysisInput input, BrandRankingProfileSnapshot profile, CancellationToken ct = default);
 }
diff --git a/src/PBA.Application/Common/Interfaces/IIdeaClusterer.cs b/src/PBA.Application/Common/Interfaces/IIdeaClusterer.cs
deleted file mode 100644
index 9fdb5c9..0000000
--- a/src/PBA.Application/Common/Interfaces/IIdeaClusterer.cs
+++ /dev/null
@@ -1,13 +0,0 @@
-namespace PBA.Application.Common.Interfaces;
-
-public sealed record ClusterInput(int Index, string Title, string? Summary);
-
-public interface IIdeaClusterer
-{
-    /// <summary>
-    /// Groups items that cover the same real-world event. Returns groups of input indices;
-    /// the first index in each group is the primary. Returns an empty list on parse failure.
-    /// </summary>
-    Task<IReadOnlyList<IReadOnlyList<int>>> ClusterAsync(
-        IReadOnlyList<ClusterInput> items, CancellationToken ct = default);
-}
diff --git a/src/PBA.Application/Common/Interfaces/ISidecarClient.cs b/src/PBA.Application/Common/Interfaces/ISidecarClient.cs
index d43f084..4fd2430 100644
--- a/src/PBA.Application/Common/Interfaces/ISidecarClient.cs
+++ b/src/PBA.Application/Common/Interfaces/ISidecarClient.cs
@@ -12,6 +12,17 @@ public interface ISidecarClient
     /// <param name="ct">Cancellation token.</param>
     Task<string> SendPromptAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default);
 
+    /// <summary>
+    /// Sends a prompt with an explicit sampling <paramref name="temperature"/> for callers that need
+    /// deterministic / low-variance output (e.g. per-pillar idea scoring, where the same item must score
+    /// consistently across sweeps). A null temperature falls back to the backend default. Honored by
+    /// <see cref="OpenRouterClient"/>; CLI-based implementations ignore it like they ignore <paramref name="model"/>.
+    /// Defined as a separate overload (not an added parameter) so existing positional callers of the
+    /// 4-argument form keep binding the trailing <see cref="CancellationToken"/> correctly.
+    /// </summary>
+    Task<string> SendPromptAsync(
+        string systemPrompt, string userPrompt, string? model, double? temperature, CancellationToken ct = default);
+
     /// <summary>
     /// Returns one embedding vector per input, in input order. Empty/whitespace inputs are skipped
     /// (never sent to the API), so the result length may be less than the input length — callers should
diff --git a/src/PBA.Application/Common/Models/BrandRankingProfileSnapshot.cs b/src/PBA.Application/Common/Models/BrandRankingProfileSnapshot.cs
new file mode 100644
index 0000000..25535ce
--- /dev/null
+++ b/src/PBA.Application/Common/Models/BrandRankingProfileSnapshot.cs
@@ -0,0 +1,63 @@
+using PBA.Domain.Entities;
+
+namespace PBA.Application.Common.Models;
+
+/// <summary>
+/// Immutable in-memory projection of one <see cref="BrandPillar"/>, carried by
+/// <see cref="BrandRankingProfileSnapshot"/>. <see cref="DescriptionEmbedding"/> is the pillar vector
+/// used by the embedding brand-fit pre-filter (section-06/07); it is null until the embedding service
+/// has populated it.
+/// </summary>
+public sealed record BrandPillarSnapshot(
+    Guid Id,
+    string Name,
+    string Description,
+    double Weight,
+    int Order,
+    float[]? DescriptionEmbedding);
+
+/// <summary>
+/// Immutable in-memory projection of the active <see cref="BrandRankingProfile"/>. Built ONCE per
+/// scoring sweep (R-H3) so every idea in a sweep is scored against the same profile and
+/// <c>ScoredProfileVersion</c> is stamped from <see cref="Version"/>, never re-read mid-sweep. The
+/// analyzer (section-05) reads positioning/audience/pillars/topics; the embedding pre-filter
+/// (section-06/07) reads pillar weights + vectors; read-time ranking (section-08) reads
+/// half-life / floor / multipliers. This is the single profile shape those layers share.
+/// </summary>
+public sealed record BrandRankingProfileSnapshot(
+    int Version,
+    string Positioning,
+    string AudiencePrimary,
+    string? AudienceSecondary,
+    IReadOnlyList<BrandPillarSnapshot> Pillars,
+    IReadOnlyList<string> AuthorityTopics,
+    IReadOnlyList<string> AntiTopics,
+    IReadOnlyList<string> VoiceMarkers,
+    double HalfLifeDays,
+    double DecayFloor,
+    double AntiTopicMultiplier,
+    double AuthorityBoost)
+{
+    /// <summary>
+    /// Projects a loaded <see cref="BrandRankingProfile"/> (with its <c>Pillars</c> populated) into an
+    /// immutable snapshot. Pillars are ordered by <see cref="BrandPillarSnapshot.Order"/> for stable
+    /// prompt rendering. Call once per sweep against the active profile.
+    /// </summary>
+    public static BrandRankingProfileSnapshot FromProfile(BrandRankingProfile profile) => new(
+        profile.Version,
+        profile.Positioning,
+        profile.AudiencePrimary,
+        profile.AudienceSecondary,
+        profile.Pillars
+            .OrderBy(p => p.Order)
+            .Select(p => new BrandPillarSnapshot(
+                p.Id, p.Name, p.Description, p.Weight, p.Order, p.DescriptionEmbedding))
+            .ToList(),
+        profile.AuthorityTopics.ToList(),
+        profile.AntiTopics.ToList(),
+        profile.VoiceMarkers.ToList(),
+        profile.HalfLifeDays,
+        profile.DecayFloor,
+        profile.AntiTopicMultiplier,
+        profile.AuthorityBoost);
+}
diff --git a/src/PBA.Application/Common/Models/IdeaAnalysis.cs b/src/PBA.Application/Common/Models/IdeaAnalysis.cs
index fdd4ce1..70556da 100644
--- a/src/PBA.Application/Common/Models/IdeaAnalysis.cs
+++ b/src/PBA.Application/Common/Models/IdeaAnalysis.cs
@@ -1,8 +1,23 @@
 namespace PBA.Application.Common.Models;
 
+/// <summary>The item the analyzer scores: raw idea fields, untouched by the LLM until scored.</summary>
+public sealed record IdeaAnalysisInput(string Title, string? Description, string? Url, string SourceName);
+
+/// <summary>
+/// One pillar's sub-score for an idea. <see cref="PillarId"/> is resolved from the snapshot by matching
+/// the LLM-returned name (R-C3) so the score survives a pillar rename; <see cref="PillarName"/> is the
+/// matched display name. <see cref="Score"/> is on the {0, .25, .5, .75, 1} scale.
+/// </summary>
+public sealed record PillarScore(Guid PillarId, string PillarName, double Score, string? Reason);
+
+/// <summary>
+/// Per-pillar analysis of an idea against the active profile snapshot. <see cref="Pillars"/> holds the
+/// raw sub-scores (stored once; query-time weights are applied later in section-08, so re-weighting
+/// never re-runs the LLM). The anti/authority flags only mark the item — the multipliers are applied at
+/// read time, not here.
+/// </summary>
 public sealed record IdeaAnalysis(
-    int Score,
-    string Reason,
-    string Summary,
-    string? Category,
-    IReadOnlyList<string> Tags);
+    IReadOnlyList<PillarScore> Pillars,
+    bool IsAntiTopic,
+    bool IsAuthorityTopic,
+    string Reason);
diff --git a/src/PBA.Infrastructure/Configuration/ClusteringOptions.cs b/src/PBA.Infrastructure/Configuration/ClusteringOptions.cs
index cb8fcba..ceb0845 100644
--- a/src/PBA.Infrastructure/Configuration/ClusteringOptions.cs
+++ b/src/PBA.Infrastructure/Configuration/ClusteringOptions.cs
@@ -5,8 +5,8 @@ public sealed class ClusteringOptions
     public const string SectionName = "Clustering";
 
     public int IntervalMinutes { get; init; } = 30;
-    public int MinScore { get; init; } = 6;
     public int LookbackHours { get; init; } = 48;
     public int MaxItemsPerSweep { get; init; } = 40;
-    public string Model { get; init; } = "google/gemini-2.5-flash";
+    // MinScore + Model removed: dedup is embedding-cosine based (DedupThreshold from RankingOptions),
+    // no LLM and no score gate.
 }
diff --git a/src/PBA.Infrastructure/Configuration/IdeaScoringOptions.cs b/src/PBA.Infrastructure/Configuration/IdeaScoringOptions.cs
index 5f1b4e6..f23b735 100644
--- a/src/PBA.Infrastructure/Configuration/IdeaScoringOptions.cs
+++ b/src/PBA.Infrastructure/Configuration/IdeaScoringOptions.cs
@@ -15,7 +15,4 @@ public sealed class IdeaScoringOptions
 
     /// <summary>Cheap, fast model for per-idea scoring. Defaults independent of the drafting model.</summary>
     public string Model { get; init; } = "google/gemini-2.5-flash";
-
-    /// <summary>When false, only ideas detected after service start are scored (no 3,831-item backfill).</summary>
-    public bool BackfillEnabled { get; init; } = false;
 }
diff --git a/src/PBA.Infrastructure/DependencyInjection.cs b/src/PBA.Infrastructure/DependencyInjection.cs
index 5404f52..f95c236 100644
--- a/src/PBA.Infrastructure/DependencyInjection.cs
+++ b/src/PBA.Infrastructure/DependencyInjection.cs
@@ -89,11 +89,11 @@ public static class DependencyInjection
         services.Configure<DigestOptions>(configuration.GetSection(DigestOptions.SectionName));
 
         services.AddScoped<IIdeaAnalyzer, PBA.Infrastructure.Services.Radar.IdeaAnalyzer>();
-        services.AddScoped<IIdeaClusterer, PBA.Infrastructure.Services.Radar.IdeaClusterer>();
+        services.AddScoped<PBA.Infrastructure.Services.Radar.IdeaEmbeddingService>();
         services.AddScoped<IDigestWriter, PBA.Infrastructure.Services.Radar.DigestWriter>();
 
         services.AddHostedService<PBA.Infrastructure.Services.Radar.IdeaScoringService>();
-        services.AddHostedService<PBA.Infrastructure.Services.Radar.IdeaClusteringService>();
+        services.AddHostedService<PBA.Infrastructure.Services.Radar.IdeaDedupService>();
         services.AddHostedService<PBA.Infrastructure.Services.Radar.DigestService>();
 
         // AI News Radar Phase 2: external delivery (email + Discord) + instant high-score alerts.
diff --git a/src/PBA.Infrastructure/Services/OpenRouterClient.cs b/src/PBA.Infrastructure/Services/OpenRouterClient.cs
index da445b9..e07d77f 100644
--- a/src/PBA.Infrastructure/Services/OpenRouterClient.cs
+++ b/src/PBA.Infrastructure/Services/OpenRouterClient.cs
@@ -28,12 +28,17 @@ public sealed class OpenRouterClient(
         DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
     };
 
-    public async Task<string> SendPromptAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default)
+    public Task<string> SendPromptAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default)
+        => SendPromptAsync(systemPrompt, userPrompt, model, temperature: null, ct);
+
+    public async Task<string> SendPromptAsync(
+        string systemPrompt, string userPrompt, string? model, double? temperature, CancellationToken ct = default)
     {
         var payload = new ChatRequest(
             model ?? _options.Model,
             [new ChatMessage("system", systemPrompt), new ChatMessage("user", userPrompt)],
-            _options.MaxTokens);
+            _options.MaxTokens,
+            temperature);
 
         var body = await PostJsonAsync("chat/completions", payload, ct);
 
@@ -173,7 +178,10 @@ public sealed class OpenRouterClient(
     private sealed record ChatRequest(
         [property: JsonPropertyName("model")] string Model,
         [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
-        [property: JsonPropertyName("max_tokens")] int MaxTokens);
+        [property: JsonPropertyName("max_tokens")] int MaxTokens,
+        // Omitted from the JSON when null (DefaultIgnoreCondition = WhenWritingNull), so existing
+        // drafting calls are byte-for-byte unchanged; only low-variance scoring sets it.
+        [property: JsonPropertyName("temperature")] double? Temperature = null);
 
     private sealed record ChatMessage(
         [property: JsonPropertyName("role")] string Role,
diff --git a/src/PBA.Infrastructure/Services/Radar/IdeaAnalyzer.cs b/src/PBA.Infrastructure/Services/Radar/IdeaAnalyzer.cs
index 51e11cf..52f04d3 100644
--- a/src/PBA.Infrastructure/Services/Radar/IdeaAnalyzer.cs
+++ b/src/PBA.Infrastructure/Services/Radar/IdeaAnalyzer.cs
@@ -1,5 +1,5 @@
+using System.Text;
 using System.Text.Json;
-using System.Text.Json.Serialization;
 using Microsoft.Extensions.Logging;
 using Microsoft.Extensions.Options;
 using PBA.Application.Common.Interfaces;
@@ -8,6 +8,11 @@ using PBA.Infrastructure.Configuration;
 
 namespace PBA.Infrastructure.Services.Radar;
 
+/// <summary>
+/// Scores an idea against the active brand profile snapshot, per pillar, on the {0,.25,.5,.75,1} scale.
+/// The LLM returns pillar NAMES; identity is the <c>BrandPillarId</c> resolved from the snapshot (R-C3),
+/// so a later rename never breaks stored sub-scores. Runs at low temperature for cross-sweep stability.
+/// </summary>
 public sealed class IdeaAnalyzer(
     ISidecarClient sidecar,
     IOptions<IdeaScoringOptions> options,
@@ -15,58 +20,116 @@ public sealed class IdeaAnalyzer(
 {
     private readonly IdeaScoringOptions _options = options.Value;
 
+    // Low temperature so the same item scores consistently sweep-to-sweep (the ranking pipeline's input).
+    private const double ScoringTemperature = 0.1;
+    private const double CollapseEpsilon = 1e-9;
+
     private static readonly JsonSerializerOptions JsonOptions = new()
     {
         PropertyNamingPolicy = JsonNamingPolicy.CamelCase
     };
 
     public async Task<IdeaAnalysis?> AnalyzeAsync(
-        string title, string? description, string? url, string sourceName, CancellationToken ct = default)
+        IdeaAnalysisInput input, BrandRankingProfileSnapshot profile, CancellationToken ct = default)
     {
-        var system = BuildSystemPrompt();
-        var user = BuildUserPrompt(title, description, url, sourceName);
+        var system = BuildSystemPrompt(profile);
+        var user = BuildUserPrompt(input);
 
-        var response = await sidecar.SendPromptAsync(system, user, _options.Model, ct);
+        var response = await sidecar.SendPromptAsync(system, user, _options.Model, ScoringTemperature, ct);
 
         var raw = Parse(response);
-        if (raw is null) return null;
+        if (raw?.Pillars is null)
+        {
+            logger.LogWarning("Idea analysis returned no parseable pillars for '{Title}'", input.Title);
+            return null;
+        }
+
+        // Resolve LLM-returned names -> BrandPillarId against the snapshot (case-insensitive, trimmed —
+        // LLMs drift on casing/whitespace). Unknown names are dropped, not invented (R-C3).
+        var byName = profile.Pillars.ToDictionary(p => p.Name.Trim(), p => p, StringComparer.OrdinalIgnoreCase);
+        var mapped = new List<PillarScore>(raw.Pillars.Count);
+        foreach (var rp in raw.Pillars)
+        {
+            if (string.IsNullOrWhiteSpace(rp.Name) || !byName.TryGetValue(rp.Name.Trim(), out var pillar))
+            {
+                logger.LogWarning("Dropping unmapped pillar name '{Name}' for '{Title}'", rp.Name, input.Title);
+                continue;
+            }
+            mapped.Add(new PillarScore(pillar.Id, pillar.Name, Math.Clamp(rp.Score, 0, 1), rp.Reason));
+        }
 
-        var score = Math.Clamp(raw.Score, 0, 10);
-        return new IdeaAnalysis(score, raw.Reason ?? "", raw.Summary ?? "", raw.Category,
-            raw.Tags ?? []);
+        if (mapped.Count == 0)
+        {
+            logger.LogWarning("No returned pillar mapped to the profile for '{Title}'", input.Title);
+            return null;
+        }
+
+        // Central-collapse guard (R-M5): an all-identical sub-score vector is the model refusing to
+        // discriminate; storing it would flatten brandFit. Only meaningful with >= 2 pillars.
+        if (mapped.Count >= 2 && mapped.All(p => Math.Abs(p.Score - mapped[0].Score) < CollapseEpsilon))
+        {
+            logger.LogWarning("Rejecting collapsed analysis (all {Count} pillars == {Score}) for '{Title}'",
+                mapped.Count, mapped[0].Score, input.Title);
+            return null;
+        }
+
+        return new IdeaAnalysis(mapped, raw.IsAntiTopic, raw.IsAuthorityTopic, raw.Reason ?? "");
+    }
+
+    private static string BuildSystemPrompt(BrandRankingProfileSnapshot p)
+    {
+        var sb = new StringBuilder();
+        sb.Append("You are a content strategist for a thought leader positioned as: ")
+          .Append(p.Positioning).Append('\n');
+        sb.Append("Primary audience: ").Append(p.AudiencePrimary).Append('\n');
+        if (!string.IsNullOrWhiteSpace(p.AudienceSecondary))
+            sb.Append("Secondary audience: ").Append(p.AudienceSecondary).Append('\n');
+
+        sb.Append("\nScore how strong a CONTENT OPPORTUNITY the item is for EACH brand pillar below, ")
+          .Append("independently. This is content-worthiness, not generic newsworthiness.\n\nPillars:\n");
+        foreach (var pillar in p.Pillars)
+            sb.Append("- ").Append(pillar.Name).Append(": ").Append(pillar.Description).Append('\n');
+
+        sb.Append("\nPer-pillar score scale:\n")
+          .Append("1.0 = a strong, ownable thought-leadership angle for this pillar\n")
+          .Append("0.75 = clearly relevant, postable for this pillar\n")
+          .Append("0.5 = tangential, would need an angle\n")
+          .Append("0.25 = weak fit\n")
+          .Append("0 = off-topic for this pillar\n");
+
+        if (p.AuthorityTopics.Count > 0)
+            sb.Append("\nAuthority topics (set isAuthorityTopic=true if the item is squarely one of these): ")
+              .Append(string.Join("; ", p.AuthorityTopics)).Append('\n');
+        if (p.AntiTopics.Count > 0)
+            sb.Append("Anti topics (set isAntiTopic=true if the item is one of these): ")
+              .Append(string.Join("; ", p.AntiTopics)).Append('\n');
+
+        sb.Append('\n').Append(FewShotAnchors).Append('\n');
+
+        sb.Append("\nScore deterministically and with low variance — the same item must score the same ")
+          .Append("way every time. Respond with ONLY a JSON object (no markdown fences, no extra text):\n")
+          .Append("""{"pillars":[{"name":"<exact pillar name>","score":0-1,"reason":"one short phrase"}],"isAntiTopic":false,"isAuthorityTopic":false,"reason":"one short sentence"}""");
+        return sb.ToString();
     }
 
-    private static string BuildSystemPrompt() =>
+    // Fixed, brand-agnostic exemplars that calibrate the 0..1 scale. Hardcoded and identical on every
+    // call (NOT derived from the snapshot) so the scale stays stable; the pillar names here are
+    // illustrative placeholders — always score the actual pillars listed above.
+    private const string FewShotAnchors =
         """
-        You are a content strategist for Matt Kruczek, an enterprise AI thought leader. His brand
-        covers enterprise AI adoption, agentic development, and AI strategy for a developer-to-executive
-        audience. Score how strong a CONTENT OPPORTUNITY a news item is for his brand, 0 to 10.
-
-        This is content-worthiness, NOT generic newsworthiness. A huge story he would never write about
-        scores low. A smaller story with an ownable, opinionated angle scores high.
-
-        Rubric:
-        9-10: a strong, ownable thought-leadership angle he could publish a great post on
-        7-8: clearly relevant, postable with a good take
-        5-6: tangentially relevant, would need an angle
-        3-4: weak fit
-        0-2: off-brand or not worth covering
-
-        Respond with ONLY a JSON object, no markdown fences, no extra text:
-        {"score": 0-10, "reason": "one short sentence", "summary": "one-sentence summary of the item",
-         "category": "short category or null", "tags": ["3-5", "keywords"]}
+        Calibration examples (illustrative; score the pillars listed above):
+        Example A — an item that is a strong, ownable angle for one pillar and off-topic for another:
+        {"pillars":[{"name":"Pillar One","score":1,"reason":"ownable angle"},{"name":"Pillar Two","score":0,"reason":"off-topic"}],"isAntiTopic":false,"isAuthorityTopic":true,"reason":"strong fit for pillar one"}
+        Example B — an item that is off-brand for every pillar:
+        {"pillars":[{"name":"Pillar One","score":0,"reason":"off-brand"},{"name":"Pillar Two","score":0.25,"reason":"weak tangent"}],"isAntiTopic":true,"isAuthorityTopic":false,"reason":"off-brand, anti-topic"}
         """;
 
-    private static string BuildUserPrompt(string title, string? description, string? url, string sourceName)
+    private static string BuildUserPrompt(IdeaAnalysisInput input)
     {
-        var lines = new List<string>
-        {
-            $"Title: {title}",
-            $"Source: {sourceName}"
-        };
-        if (!string.IsNullOrWhiteSpace(url)) lines.Add($"URL: {url}");
-        if (!string.IsNullOrWhiteSpace(description))
-            lines.Add($"Content: {description[..Math.Min(1000, description.Length)]}");
+        var lines = new List<string> { $"Title: {input.Title}", $"Source: {input.SourceName}" };
+        if (!string.IsNullOrWhiteSpace(input.Url)) lines.Add($"URL: {input.Url}");
+        if (!string.IsNullOrWhiteSpace(input.Description))
+            lines.Add($"Content: {input.Description[..Math.Min(1000, input.Description.Length)]}");
         return string.Join('\n', lines);
     }
 
@@ -85,6 +148,7 @@ public sealed class IdeaAnalyzer(
         }
     }
 
+    // The model still occasionally wraps output in ``` fences despite the instruction; tolerate it.
     private static string StripFences(string response)
     {
         var json = response.Trim();
@@ -97,9 +161,10 @@ public sealed class IdeaAnalyzer(
     }
 
     private sealed record Raw(
-        int Score,
-        [property: JsonPropertyName("reason")] string? Reason,
-        [property: JsonPropertyName("summary")] string? Summary,
-        [property: JsonPropertyName("category")] string? Category,
-        [property: JsonPropertyName("tags")] List<string>? Tags);
+        List<RawPillar>? Pillars,
+        bool IsAntiTopic,
+        bool IsAuthorityTopic,
+        string? Reason);
+
+    private sealed record RawPillar(string? Name, double Score, string? Reason);
 }
diff --git a/src/PBA.Infrastructure/Services/Radar/IdeaClusterer.cs b/src/PBA.Infrastructure/Services/Radar/IdeaClusterer.cs
deleted file mode 100644
index 3edfa6b..0000000
--- a/src/PBA.Infrastructure/Services/Radar/IdeaClusterer.cs
+++ /dev/null
@@ -1,81 +0,0 @@
-using System.Text;
-using System.Text.Json;
-using System.Text.Json.Serialization;
-using Microsoft.Extensions.Logging;
-using Microsoft.Extensions.Options;
-using PBA.Application.Common.Interfaces;
-using PBA.Infrastructure.Configuration;
-
-namespace PBA.Infrastructure.Services.Radar;
-
-public sealed class IdeaClusterer(
-    ISidecarClient sidecar,
-    IOptions<ClusteringOptions> options,
-    ILogger<IdeaClusterer> logger) : IIdeaClusterer
-{
-    private readonly ClusteringOptions _options = options.Value;
-
-    private static readonly JsonSerializerOptions JsonOptions = new()
-    {
-        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
-    };
-
-    public async Task<IReadOnlyList<IReadOnlyList<int>>> ClusterAsync(
-        IReadOnlyList<ClusterInput> items, CancellationToken ct = default)
-    {
-        if (items.Count < 2) return [];
-
-        var response = await sidecar.SendPromptAsync(System, BuildUser(items), _options.Model, ct);
-
-        try
-        {
-            var parsed = JsonSerializer.Deserialize<Result>(StripFences(response), JsonOptions);
-            return parsed?.Groups?
-                .Where(g => g.Count >= 2)
-                .Select(g => (IReadOnlyList<int>)g)
-                .ToList() ?? [];
-        }
-        catch (JsonException ex)
-        {
-            logger.LogWarning(ex, "Failed to parse clustering JSON");
-            return [];
-        }
-    }
-
-    private const string System =
-        """
-        You are a news deduplication assistant. Group items that report the IDENTICAL real-world
-        event (for example the same product release announced in different outlets). Do NOT group
-        items that merely share a topic but cover different events. When unsure, keep them separate.
-
-        Respond with ONLY JSON, no fences:
-        {"groups": [[0, 3], [5, 7, 9]]}
-        Each inner array lists the input indices of one event. The first index is the primary.
-        Only include groups of 2 or more. Omit singletons.
-        """;
-
-    private static string BuildUser(IReadOnlyList<ClusterInput> items)
-    {
-        var sb = new StringBuilder("Items:\n");
-        foreach (var item in items)
-        {
-            sb.Append('[').Append(item.Index).Append("] ").Append(item.Title);
-            if (!string.IsNullOrWhiteSpace(item.Summary)) sb.Append(" -- ").Append(item.Summary);
-            sb.Append('\n');
-        }
-        return sb.ToString();
-    }
-
-    private static string StripFences(string response)
-    {
-        var json = response.Trim();
-        if (!json.StartsWith("```")) return json;
-        var firstNewline = json.IndexOf('\n');
-        if (firstNewline >= 0) json = json[(firstNewline + 1)..];
-        var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
-        if (lastFence >= 0) json = json[..lastFence];
-        return json.Trim();
-    }
-
-    private sealed record Result([property: JsonPropertyName("groups")] List<List<int>>? Groups);
-}
diff --git a/src/PBA.Infrastructure/Services/Radar/IdeaClusteringService.cs b/src/PBA.Infrastructure/Services/Radar/IdeaClusteringService.cs
deleted file mode 100644
index 76d50eb..0000000
--- a/src/PBA.Infrastructure/Services/Radar/IdeaClusteringService.cs
+++ /dev/null
@@ -1,80 +0,0 @@
-using Microsoft.EntityFrameworkCore;
-using Microsoft.Extensions.DependencyInjection;
-using Microsoft.Extensions.Hosting;
-using Microsoft.Extensions.Logging;
-using Microsoft.Extensions.Options;
-using PBA.Application.Common.Interfaces;
-using PBA.Infrastructure.Configuration;
-using PBA.Infrastructure.Data;
-
-namespace PBA.Infrastructure.Services.Radar;
-
-public sealed class IdeaClusteringService(
-    IServiceScopeFactory scopeFactory,
-    IOptions<ClusteringOptions> options,
-    ILogger<IdeaClusteringService> logger) : BackgroundService
-{
-    private readonly ClusteringOptions _options = options.Value;
-
-    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
-    {
-        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
-
-        while (!stoppingToken.IsCancellationRequested)
-        {
-            try
-            {
-                await ClusterBatchAsync(stoppingToken);
-            }
-            catch (Exception ex) when (ex is not OperationCanceledException)
-            {
-                logger.LogError(ex, "Idea clustering sweep failed");
-            }
-
-            await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken);
-        }
-    }
-
-    internal async Task ClusterBatchAsync(CancellationToken ct)
-    {
-        using var scope = scopeFactory.CreateScope();
-        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
-        var clusterer = scope.ServiceProvider.GetRequiredService<IIdeaClusterer>();
-
-        var since = DateTimeOffset.UtcNow.AddHours(-_options.LookbackHours);
-        var candidates = await db.Ideas
-            .Where(i => i.ClusteredAt == null
-                && i.ScoredAt != null
-                && i.Score >= _options.MinScore
-                && i.DuplicateOfId == null
-                && i.DetectedAt >= since)
-            .OrderByDescending(i => i.Score)
-            .Take(_options.MaxItemsPerSweep)
-            .ToListAsync(ct);
-
-        if (candidates.Count < 2) return;
-
-        var inputs = candidates
-            .Select((idea, idx) => new ClusterInput(idx, idea.Title, idea.Summary))
-            .ToList();
-
-        var groups = await clusterer.ClusterAsync(inputs, ct);
-
-        foreach (var group in groups)
-        {
-            if (group.Count < 2) continue;
-            var primary = candidates[group[0]];
-            foreach (var dupIdx in group.Skip(1))
-            {
-                if (dupIdx < 0 || dupIdx >= candidates.Count) continue;
-                candidates[dupIdx].DuplicateOfId = primary.Id;
-            }
-        }
-
-        var now = DateTimeOffset.UtcNow;
-        foreach (var idea in candidates) idea.ClusteredAt = now;
-
-        await db.SaveChangesAsync(ct);
-        logger.LogInformation("Clustered {Count} ideas into {Groups} groups", candidates.Count, groups.Count);
-    }
-}
diff --git a/src/PBA.Infrastructure/Services/Radar/IdeaDedupService.cs b/src/PBA.Infrastructure/Services/Radar/IdeaDedupService.cs
new file mode 100644
index 0000000..33c8ce3
--- /dev/null
+++ b/src/PBA.Infrastructure/Services/Radar/IdeaDedupService.cs
@@ -0,0 +1,106 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common;
+using PBA.Domain.Entities;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Data;
+
+namespace PBA.Infrastructure.Services.Radar;
+
+/// <summary>
+/// Marks near-duplicate ideas using in-memory cosine similarity over embeddings (replaces the old
+/// LLM clusterer). Gated on full embedding coverage of the window (R-H1) so it never picks a wrong
+/// primary mid-backfill; within a group the highest-brandFit idea is primary. No LLM/sidecar use.
+/// </summary>
+public sealed class IdeaDedupService(
+    IServiceScopeFactory scopeFactory,
+    IOptions<ClusteringOptions> options,
+    IOptionsMonitor<RankingOptions> rankingOptions,
+    ILogger<IdeaDedupService> logger) : BackgroundService
+{
+    private readonly ClusteringOptions _options = options.Value;
+
+    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
+    {
+        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
+
+        while (!stoppingToken.IsCancellationRequested)
+        {
+            try
+            {
+                await DedupBatchAsync(stoppingToken);
+            }
+            catch (Exception ex) when (ex is not OperationCanceledException)
+            {
+                logger.LogError(ex, "Idea dedup sweep failed");
+            }
+
+            await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken);
+        }
+    }
+
+    internal async Task DedupBatchAsync(CancellationToken ct)
+    {
+        using var scope = scopeFactory.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
+        var dedupThreshold = rankingOptions.CurrentValue.DedupThreshold; // snapshot once per sweep
+
+        var since = DateTimeOffset.UtcNow.AddHours(-_options.LookbackHours);
+
+        // Gate (R-H1): if any in-window idea is still unembedded, dedup could pick a wrong primary; bail.
+        if (await db.Ideas.AnyAsync(i => i.DetectedAt >= since && i.Embedding == null, ct))
+        {
+            logger.LogInformation("Dedup gated: in-window ideas still unembedded");
+            return;
+        }
+
+        var candidates = await db.Ideas
+            .Where(i => i.Embedding != null
+                && i.DetectedAt >= since
+                && i.DuplicateOfId == null
+                && i.ClusteredAt == null)
+            .OrderByDescending(i => i.Score)
+            .Take(_options.MaxItemsPerSweep)
+            .ToListAsync(ct);
+        if (candidates.Count < 2) return;
+
+        // In-memory pairwise cosine grouping via union-find (R-L3-dedup). O(n²) over ~MaxItemsPerSweep
+        // (~40) is trivial; no pgvector query (EF InMemory can't run one).
+        var parent = Enumerable.Range(0, candidates.Count).ToArray();
+        int Find(int x)
+        {
+            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
+            return x;
+        }
+
+        for (var i = 0; i < candidates.Count; i++)
+        {
+            for (var j = i + 1; j < candidates.Count; j++)
+            {
+                if (VectorMath.CosineSimilarity(candidates[i].Embedding!, candidates[j].Embedding!) >= dedupThreshold)
+                    parent[Find(i)] = Find(j);
+            }
+        }
+
+        foreach (var group in candidates.Select((idea, idx) => (idea, root: Find(idx))).GroupBy(x => x.root))
+        {
+            var members = group.Select(x => x.idea).ToList();
+            if (members.Count < 2) continue;
+
+            // Primary = highest brandFit. The derived Score is round(brandFit×10) for both LLM-scored and
+            // embedding-only items, so it is a faithful brandFit proxy without re-snapshotting the profile.
+            var primary = members.OrderByDescending(m => m.Score ?? 0).First();
+            foreach (var dup in members.Where(m => m.Id != primary.Id))
+                dup.DuplicateOfId = primary.Id;
+        }
+
+        var now = DateTimeOffset.UtcNow;
+        foreach (var idea in candidates) idea.ClusteredAt = now;
+
+        await db.SaveChangesAsync(ct);
+        logger.LogInformation("Dedup sweep: processed {Count} in-window ideas", candidates.Count);
+    }
+}
diff --git a/src/PBA.Infrastructure/Services/Radar/IdeaEmbeddingService.cs b/src/PBA.Infrastructure/Services/Radar/IdeaEmbeddingService.cs
new file mode 100644
index 0000000..0366625
--- /dev/null
+++ b/src/PBA.Infrastructure/Services/Radar/IdeaEmbeddingService.cs
@@ -0,0 +1,152 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Entities;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Data;
+
+namespace PBA.Infrastructure.Services.Radar;
+
+/// <summary>
+/// Turns ideas and active-profile pillar descriptions into stored <c>vector(1536)</c> embeddings, and
+/// exposes the embedding brand-fit pre-filter the scoring sweep (section-07) consumes. Does no LLM
+/// scoring or dedup. Never persists a zero/NaN vector (R-H2); a failed embedding batch leaves its items
+/// <c>Embedding == null</c> for retry rather than aborting the whole pass.
+/// </summary>
+public sealed class IdeaEmbeddingService(
+    ApplicationDbContext db,
+    ISidecarClient sidecar,
+    IOptionsMonitor<EmbeddingOptions> embeddingOptions,
+    ILogger<IdeaEmbeddingService> logger)
+{
+    /// <summary>
+    /// Embeds every idea with <c>Embedding == null</c> (title + description) and embeds active-profile
+    /// pillar descriptions whose vector is still null. Idempotent: already-embedded ideas/pillars are
+    /// not re-sent. Persists once per source after the batch loop.
+    /// </summary>
+    public async Task EmbedPendingAsync(CancellationToken ct)
+    {
+        var options = embeddingOptions.CurrentValue;
+        await EmbedIdeasAsync(options, ct);
+        await EmbedActivePillarsAsync(options, ct);
+    }
+
+    /// <summary>
+    /// Embedding brand-fit pre-filter for one item over the given pillars. Delegates to the single shared
+    /// <see cref="BrandFit.EmbeddingWeightedSum"/> so the sweep and this service never diverge.
+    /// </summary>
+    public double ComputeEmbeddingBrandFit(float[] itemVec, IReadOnlyList<BrandPillar> pillars) =>
+        BrandFit.EmbeddingWeightedSum(
+            itemVec, pillars.Select(p => (p.Weight, p.DescriptionEmbedding)).ToList());
+
+    private async Task EmbedIdeasAsync(EmbeddingOptions options, CancellationToken ct)
+    {
+        var pending = await db.Ideas.Where(i => i.Embedding == null).ToListAsync(ct);
+        if (pending.Count == 0) return;
+
+        var now = DateTimeOffset.UtcNow;
+        var changed = false;
+
+        // Embed in safe chunks: EmbedAsync throws on a failing batch, so wrap each chunk so one failure
+        // leaves only that chunk's items unembedded for retry (R-H2). EmbedAsync also drops empty inputs,
+        // so build (idea, text) pairs filtered to non-empty text and align vectors by that filtered order
+        // — never positional over the raw chunk.
+        foreach (var chunk in pending.Chunk(options.BatchSize))
+        {
+            var items = chunk
+                .Select(i => (idea: i, text: BuildText(i)))
+                .Where(x => !string.IsNullOrWhiteSpace(x.text))
+                .ToList();
+            if (items.Count == 0) continue;
+
+            IReadOnlyList<float[]> vectors;
+            try
+            {
+                vectors = await sidecar.EmbedAsync(items.Select(x => x.text).ToList(), options.Model, ct);
+            }
+            catch (Exception ex) when (ex is not OperationCanceledException)
+            {
+                logger.LogWarning(ex, "Idea embedding batch failed; {Count} ideas left for retry", items.Count);
+                continue;
+            }
+
+            if (vectors.Count != items.Count)
+            {
+                logger.LogWarning("Idea embedding returned {Got} vectors for {Sent} inputs; skipping chunk",
+                    vectors.Count, items.Count);
+                continue;
+            }
+
+            for (var i = 0; i < items.Count; i++)
+            {
+                if (!IsUsable(vectors[i])) continue; // never persist a zero/NaN vector (R-H2)
+                items[i].idea.Embedding = vectors[i];
+                items[i].idea.EmbeddedAt = now;
+                changed = true;
+            }
+        }
+
+        if (changed) await db.SaveChangesAsync(ct);
+    }
+
+    private async Task EmbedActivePillarsAsync(EmbeddingOptions options, CancellationToken ct)
+    {
+        var profile = await db.BrandRankingProfiles
+            .Include(p => p.Pillars)
+            .FirstOrDefaultAsync(p => p.IsActive, ct);
+        if (profile is null) return;
+
+        // Stale = no vector yet. A pillar-definition edit (section-09) nulls the affected pillar's vector
+        // and bumps Version, so a null vector is the single "needs (re)embedding" signal.
+        var stale = profile.Pillars
+            .Where(p => p.DescriptionEmbedding == null && !string.IsNullOrWhiteSpace(p.Description))
+            .ToList();
+        if (stale.Count == 0) return;
+
+        IReadOnlyList<float[]> vectors;
+        try
+        {
+            vectors = await sidecar.EmbedAsync(stale.Select(p => p.Description).ToList(), options.Model, ct);
+        }
+        catch (Exception ex) when (ex is not OperationCanceledException)
+        {
+            logger.LogWarning(ex, "Pillar embedding batch failed; {Count} pillars left for retry", stale.Count);
+            return;
+        }
+
+        if (vectors.Count != stale.Count)
+        {
+            logger.LogWarning("Pillar embedding returned {Got} vectors for {Sent} inputs; skipping",
+                vectors.Count, stale.Count);
+            return;
+        }
+
+        var changed = false;
+        for (var i = 0; i < stale.Count; i++)
+        {
+            if (!IsUsable(vectors[i])) continue; // never persist a zero/NaN vector (R-H2)
+            stale[i].DescriptionEmbedding = vectors[i];
+            changed = true;
+        }
+
+        if (changed) await db.SaveChangesAsync(ct);
+    }
+
+    private static string BuildText(Idea idea) => $"{idea.Title} {idea.Description}".Trim();
+
+    // Reject zero / NaN / Infinity vectors at the persist boundary (R-H2): cosine vs a zero vector is
+    // undefined and corrupts dedup/pre-filter, so the item stays null for a later retry.
+    private static bool IsUsable(float[]? vector)
+    {
+        if (vector is null || vector.Length == 0) return false;
+        var allZero = true;
+        foreach (var v in vector)
+        {
+            if (!float.IsFinite(v)) return false;
+            if (v != 0f) allZero = false;
+        }
+        return !allZero;
+    }
+}
diff --git a/src/PBA.Infrastructure/Services/Radar/IdeaScoringService.cs b/src/PBA.Infrastructure/Services/Radar/IdeaScoringService.cs
index 251de01..6dd0678 100644
--- a/src/PBA.Infrastructure/Services/Radar/IdeaScoringService.cs
+++ b/src/PBA.Infrastructure/Services/Radar/IdeaScoringService.cs
@@ -3,31 +3,42 @@ using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Hosting;
 using Microsoft.Extensions.Logging;
 using Microsoft.Extensions.Options;
+using PBA.Application.Common;
 using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Domain.Entities;
 using PBA.Infrastructure.Configuration;
 using PBA.Infrastructure.Data;
 
 namespace PBA.Infrastructure.Services.Radar;
 
+/// <summary>
+/// Brand-anchored scoring sweep. Each sweep snapshots the active profile + ranking options ONCE
+/// (R-H3, R-L2), ensures pending ideas/pillars are embedded, builds an embedded + in-window +
+/// not-current-version candidate set, computes an embedding brand-fit pre-filter, LLM per-pillar scores
+/// the above-threshold items (bounded by BatchSize), and stamps embedding-only brandFit on the rest.
+/// </summary>
 public sealed class IdeaScoringService(
     IServiceScopeFactory scopeFactory,
     IOptions<IdeaScoringOptions> options,
+    IOptionsMonitor<RankingOptions> rankingOptions,
     ILogger<IdeaScoringService> logger) : BackgroundService
 {
     private readonly IdeaScoringOptions _options = options.Value;
-    private DateTimeOffset _startedAt;
+
+    // A poison item (refusal / unparseable / central-collapse) must not burn an LLM call every sweep
+    // forever; candidates at/above the cap are excluded (R-M5).
+    private const int AttemptCap = 3;
 
     protected override async Task ExecuteAsync(CancellationToken stoppingToken)
     {
-        _startedAt = DateTimeOffset.UtcNow;
         await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
 
         while (!stoppingToken.IsCancellationRequested)
         {
             try
             {
-                var cutoff = _options.BackfillEnabled ? (DateTimeOffset?)null : _startedAt;
-                await ScoreBatchAsync(cutoff, stoppingToken);
+                await ScoreSweepAsync(stoppingToken);
             }
             catch (Exception ex) when (ex is not OperationCanceledException)
             {
@@ -38,46 +49,103 @@ public sealed class IdeaScoringService(
         }
     }
 
-    internal async Task ScoreBatchAsync(DateTimeOffset? backfillCutoff, CancellationToken ct)
+    internal async Task ScoreSweepAsync(CancellationToken ct)
     {
         using var scope = scopeFactory.CreateScope();
         var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
         var analyzer = scope.ServiceProvider.GetRequiredService<IIdeaAnalyzer>();
+        var embedder = scope.ServiceProvider.GetRequiredService<IdeaEmbeddingService>();
+
+        // Embed pending ideas + pillar descriptions first so the candidate set and pre-filter see vectors.
+        await embedder.EmbedPendingAsync(ct);
 
-        var query = db.Ideas.Where(i => i.ScoredAt == null);
-        if (backfillCutoff is { } cutoff)
-            query = query.Where(i => i.DetectedAt >= cutoff);
+        // Snapshot the active profile ONCE (R-H3): ScoredProfileVersion is stamped from this snapshot,
+        // never re-read mid-sweep.
+        var profileEntity = await db.BrandRankingProfiles
+            .Include(p => p.Pillars)
+            .FirstOrDefaultAsync(p => p.IsActive, ct);
+        if (profileEntity is null)
+        {
+            logger.LogInformation("No active brand ranking profile; skipping scoring sweep");
+            return;
+        }
 
-        var batch = await query
-            .OrderByDescending(i => i.DetectedAt)
-            .Take(_options.BatchSize)
+        var snapshot = BrandRankingProfileSnapshot.FromProfile(profileEntity);
+        var ranking = rankingOptions.CurrentValue; // snapshot options ONCE per sweep (R-L2)
+
+        var now = DateTimeOffset.UtcNow;
+        var windowStart = now.AddDays(-ranking.ScoringWindowDays);
+
+        var candidates = await db.Ideas
+            .Where(i => i.Embedding != null
+                && i.DetectedAt >= windowStart
+                && (i.ScoredProfileVersion == null || i.ScoredProfileVersion < snapshot.Version)
+                && i.ScoreAttempts < AttemptCap)
             .ToListAsync(ct);
+        if (candidates.Count == 0) return;
+
+        var pillarVectors = snapshot.Pillars.Select(p => (p.Weight, p.DescriptionEmbedding)).ToList();
+        var pillarWeights = snapshot.Pillars.Select(p => (p.Id, p.Weight)).ToList();
 
-        if (batch.Count == 0) return;
+        // Pre-filter (in memory) via the single shared brand-fit helper.
+        var prefiltered = candidates
+            .Select(i => (idea: i, fit: BrandFit.EmbeddingWeightedSum(i.Embedding!, pillarVectors)))
+            .ToList();
 
-        var scored = 0;
-        foreach (var idea in batch)
+        var above = prefiltered
+            .Where(x => x.fit >= ranking.PreFilterThreshold)
+            .OrderByDescending(x => x.fit)
+            .Take(_options.BatchSize) // BatchSize bounds the LLM-scored subset
+            .ToList();
+        var below = prefiltered.Where(x => x.fit < ranking.PreFilterThreshold).ToList();
+
+        var changed = false;
+        var llmScored = 0;
+
+        foreach (var (idea, _) in above)
         {
+            idea.ScoreAttempts += 1; // count every attempt so poison items are bounded (R-M5)
+            changed = true;
+
             var analysis = await analyzer.AnalyzeAsync(
-                idea.Title, idea.Description, idea.Url, idea.SourceName, ct);
+                new IdeaAnalysisInput(idea.Title, idea.Description, idea.Url, idea.SourceName), snapshot, ct);
 
             if (analysis is not null)
             {
-                idea.Score = analysis.Score;
+                idea.PillarSubScores = analysis.Pillars
+                    .Select(p => new PillarSubScore
+                    {
+                        PillarId = p.PillarId, PillarName = p.PillarName, Score = p.Score, Reason = p.Reason
+                    })
+                    .ToList();
+                idea.IsAntiTopic = analysis.IsAntiTopic;
+                idea.IsAuthorityTopic = analysis.IsAuthorityTopic;
                 idea.ScoreReason = analysis.Reason;
-                idea.Summary = analysis.Summary;
-                idea.Category ??= analysis.Category;
-                if (analysis.Tags.Count > 0) idea.Tags = analysis.Tags.ToList();
-                idea.ScoredAt = DateTimeOffset.UtcNow;
-                scored++;
+                idea.ScoredProfileVersion = snapshot.Version;
+                idea.ScoredAt = now;
+
+                var subById = analysis.Pillars.ToDictionary(p => p.PillarId, p => p.Score);
+                var llmBrandFit = BrandFit.RenormalizedSubScore(subById, pillarWeights);
+                idea.Score = (int)Math.Round(Math.Clamp(llmBrandFit, 0, 1) * 10); // derived display value (R-M1)
+                llmScored++;
             }
+            // analysis == null: leave sub-scores empty and do NOT stamp ScoredProfileVersion — it retries
+            // next sweep, bounded by the already-incremented ScoreAttempts.
 
-            if (_options.ThrottleMs > 0)
-                await Task.Delay(_options.ThrottleMs, ct);
+            if (_options.ThrottleMs > 0) await Task.Delay(_options.ThrottleMs, ct);
+        }
+
+        foreach (var (idea, fit) in below)
+        {
+            // No LLM call. Stamp the version so it is not reconsidered until the profile changes.
+            idea.ScoredProfileVersion = snapshot.Version;
+            idea.ScoredAt = now;
+            idea.Score = (int)Math.Round(Math.Clamp(fit, 0, 1) * 10); // embedding-only brandFit (R-M1)
+            changed = true;
         }
 
-        if (scored > 0)
-            await db.SaveChangesAsync(ct);
-        logger.LogInformation("Scored {Scored}/{Total} ideas", scored, batch.Count);
+        if (changed) await db.SaveChangesAsync(ct);
+        logger.LogInformation("Scoring sweep: {Llm} LLM-scored, {Below} embedding-only of {Total} candidates",
+            llmScored, below.Count, candidates.Count);
     }
 }
diff --git a/src/PBA.Infrastructure/Services/SidecarClient.cs b/src/PBA.Infrastructure/Services/SidecarClient.cs
index f3fe348..d92eebb 100644
--- a/src/PBA.Infrastructure/Services/SidecarClient.cs
+++ b/src/PBA.Infrastructure/Services/SidecarClient.cs
@@ -60,6 +60,11 @@ public class SidecarClient : ISidecarClient, IDisposable
         }
     }
 
+    // The CLI backend cannot switch sampling temperature; it is ignored, like the model override.
+    public Task<string> SendPromptAsync(
+        string systemPrompt, string userPrompt, string? model, double? temperature, CancellationToken ct = default)
+        => SendPromptAsync(systemPrompt, userPrompt, model, ct);
+
     // The CLI sidecar cannot produce embeddings; embeddings route through OpenRouterClient.
     public Task<IReadOnlyList<float[]>> EmbedAsync(
         IReadOnlyList<string> inputs, string? model = null, CancellationToken ct = default)
diff --git a/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaAnalyzerTests.cs b/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaAnalyzerTests.cs
index d022e80..8dc97ad 100644
--- a/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaAnalyzerTests.cs
+++ b/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaAnalyzerTests.cs
@@ -1,7 +1,8 @@
-using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Logging;
 using Microsoft.Extensions.Options;
 using Moq;
 using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
 using PBA.Infrastructure.Configuration;
 using PBA.Infrastructure.Services.Radar;
 using Xunit;
@@ -10,66 +11,212 @@ namespace PBA.Infrastructure.Tests.Services.Radar;
 
 public class IdeaAnalyzerTests
 {
-    private static IdeaAnalyzer Build(string llmResponse, out Mock<ISidecarClient> sidecar)
+    private static readonly Guid P1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
+    private static readonly Guid P2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
+
+    private static BrandRankingProfileSnapshot Profile() => new(
+        Version: 3,
+        Positioning: "Enterprise AI thought leader who ships",
+        AudiencePrimary: "Senior engineers and architects",
+        AudienceSecondary: "Executives and decision-makers",
+        Pillars:
+        [
+            new BrandPillarSnapshot(P1, "Enterprise AI Adoption",
+                "Strategy, governance, ROI of enterprise AI", 0.6, 0, null),
+            new BrandPillarSnapshot(P2, "Agentic Development",
+                "Building with agents, MCP, harnesses", 0.4, 1, null),
+        ],
+        AuthorityTopics: ["MCP server design"],
+        AntiTopics: ["Crypto / web3 coins"],
+        VoiceMarkers: ["No em dashes"],
+        HalfLifeDays: 7, DecayFloor: 0.075, AntiTopicMultiplier: 0.1, AuthorityBoost: 1.2);
+
+    private static IdeaAnalysisInput Input() => new("Title", "Desc", "http://x", "Source");
+
+    private static IdeaAnalyzer Build(
+        string response,
+        out Mock<ISidecarClient> sidecar,
+        out Mock<ILogger<IdeaAnalyzer>> logger,
+        List<string>? capturedSystems = null,
+        List<double?>? capturedTemps = null)
     {
         sidecar = new Mock<ISidecarClient>();
         sidecar.Setup(s => s.SendPromptAsync(
-                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
-            .ReturnsAsync(llmResponse);
+                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(),
+                It.IsAny<CancellationToken>()))
+            .Callback<string, string, string?, double?, CancellationToken>((sys, _, _, temp, _) =>
+            {
+                capturedSystems?.Add(sys);
+                capturedTemps?.Add(temp);
+            })
+            .ReturnsAsync(response);
+        logger = new Mock<ILogger<IdeaAnalyzer>>();
         var options = Options.Create(new IdeaScoringOptions { Model = "cheap-model" });
-        return new IdeaAnalyzer(sidecar.Object, options, NullLogger<IdeaAnalyzer>.Instance);
+        return new IdeaAnalyzer(sidecar.Object, options, logger.Object);
     }
 
+    private static void VerifyWarning(Mock<ILogger<IdeaAnalyzer>> logger, Times times) =>
+        logger.Verify(l => l.Log(
+            LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
+            It.IsAny<Exception?>(),
+            (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()), times);
+
     [Fact]
-    public async Task AnalyzeAsync_ValidJson_ReturnsParsedAnalysis()
+    public async Task AnalyzeAsync_StructuredJson_ParsesPerPillarSubScores()
     {
         var analyzer = Build(
-            """{"score":8,"reason":"Strong angle","summary":"One line","category":"AI","tags":["agents","enterprise"]}""",
-            out _);
+            """{"pillars":[{"name":"Enterprise AI Adoption","score":0.75,"reason":"ownable"},{"name":"Agentic Development","score":0.25,"reason":"tangential"}],"isAntiTopic":false,"isAuthorityTopic":true,"reason":"good fit"}""",
+            out _, out _);
 
-        var result = await analyzer.AnalyzeAsync("Title", "Desc", "http://x", "Source");
+        var result = await analyzer.AnalyzeAsync(Input(), Profile());
 
         Assert.NotNull(result);
-        Assert.Equal(8, result!.Score);
-        Assert.Equal("Strong angle", result.Reason);
-        Assert.Equal("One line", result.Summary);
-        Assert.Equal("AI", result.Category);
-        Assert.Equal(new[] { "agents", "enterprise" }, result.Tags);
+        Assert.Equal(2, result!.Pillars.Count);
+        var p1 = result.Pillars.Single(p => p.PillarId == P1);
+        Assert.Equal(0.75, p1.Score);
+        Assert.Equal("Enterprise AI Adoption", p1.PillarName);
+        Assert.Equal(0.25, result.Pillars.Single(p => p.PillarId == P2).Score);
     }
 
     [Fact]
-    public async Task AnalyzeAsync_PassesConfiguredCheapModel()
+    public async Task AnalyzeAsync_LlmPillarNames_MapToBrandPillarId()
     {
-        var analyzer = Build("""{"score":5,"reason":"r","summary":"s","category":null,"tags":[]}""", out var sidecar);
+        // Case + whitespace drift on the returned names must still map (R-C3).
+        var analyzer = Build(
+            """{"pillars":[{"name":"  enterprise ai adoption ","score":1,"reason":"r"},{"name":"AGENTIC DEVELOPMENT","score":0.5,"reason":"r"}],"isAntiTopic":false,"isAuthorityTopic":false,"reason":"r"}""",
+            out _, out _);
 
-        await analyzer.AnalyzeAsync("T", null, null, "S");
+        var result = await analyzer.AnalyzeAsync(Input(), Profile());
 
-        sidecar.Verify(s => s.SendPromptAsync(
-            It.IsAny<string>(), It.IsAny<string>(), "cheap-model", It.IsAny<CancellationToken>()), Times.Once);
+        Assert.NotNull(result);
+        Assert.Equal(P1, result!.Pillars.Single(p => Math.Abs(p.Score - 1) < 1e-9).PillarId);
+        Assert.Equal(P2, result.Pillars.Single(p => Math.Abs(p.Score - 0.5) < 1e-9).PillarId);
+    }
+
+    [Fact]
+    public async Task AnalyzeAsync_UnknownPillarName_IsDroppedAndWarned()
+    {
+        var analyzer = Build(
+            """{"pillars":[{"name":"Enterprise AI Adoption","score":0.75,"reason":"r"},{"name":"Totally Made Up","score":1,"reason":"r"}],"isAntiTopic":false,"isAuthorityTopic":false,"reason":"r"}""",
+            out _, out var logger);
+
+        var result = await analyzer.AnalyzeAsync(Input(), Profile());
+
+        Assert.NotNull(result);
+        Assert.Single(result!.Pillars);
+        Assert.Equal(P1, result.Pillars[0].PillarId);
+        VerifyWarning(logger, Times.AtLeastOnce());
+    }
+
+    [Fact]
+    public async Task AnalyzeAsync_ExtractsFlagsAndPerPillarReason()
+    {
+        var analyzer = Build(
+            """{"pillars":[{"name":"Enterprise AI Adoption","score":0.75,"reason":"ownable angle"},{"name":"Agentic Development","score":0.25,"reason":"tangential"}],"isAntiTopic":true,"isAuthorityTopic":true,"reason":"overall reason"}""",
+            out _, out _);
+
+        var result = await analyzer.AnalyzeAsync(Input(), Profile());
+
+        Assert.NotNull(result);
+        Assert.True(result!.IsAntiTopic);
+        Assert.True(result.IsAuthorityTopic);
+        Assert.Equal("overall reason", result.Reason);
+        Assert.Equal("ownable angle", result.Pillars.Single(p => p.PillarId == P1).Reason);
     }
 
     [Fact]
-    public async Task AnalyzeAsync_MalformedJson_ReturnsNull()
+    public async Task AnalyzeAsync_SystemPrompt_IsBuiltFromSnapshot()
     {
-        var analyzer = Build("not json at all", out _);
-        var result = await analyzer.AnalyzeAsync("T", null, null, "S");
-        Assert.Null(result);
+        var systems = new List<string>();
+        var analyzer = Build(
+            """{"pillars":[{"name":"Enterprise AI Adoption","score":0.75,"reason":"r"},{"name":"Agentic Development","score":0.25,"reason":"r"}],"isAntiTopic":false,"isAuthorityTopic":false,"reason":"r"}""",
+            out _, out _, capturedSystems: systems);
+
+        await analyzer.AnalyzeAsync(Input(), Profile());
+
+        var system = Assert.Single(systems);
+        Assert.Contains("Enterprise AI thought leader who ships", system);   // positioning
+        Assert.Contains("Senior engineers and architects", system);          // audience
+        Assert.Contains("Enterprise AI Adoption", system);                   // pillar name
+        Assert.Contains("Strategy, governance, ROI of enterprise AI", system); // pillar description
+        Assert.Contains("Agentic Development", system);
+        Assert.Contains("MCP server design", system);                        // authority topic
+        Assert.Contains("Crypto / web3 coins", system);                      // anti topic
+        Assert.Contains("0.75 = clearly relevant", system);                  // per-level rubric
+    }
+
+    [Fact]
+    public async Task AnalyzeAsync_IncludesFixedFewShotAnchors_StableAcrossCalls()
+    {
+        var systems = new List<string>();
+        var analyzer = Build(
+            """{"pillars":[{"name":"Enterprise AI Adoption","score":0.75,"reason":"r"},{"name":"Agentic Development","score":0.25,"reason":"r"}],"isAntiTopic":false,"isAuthorityTopic":false,"reason":"r"}""",
+            out _, out _, capturedSystems: systems);
+
+        await analyzer.AnalyzeAsync(new IdeaAnalysisInput("First", "a", null, "S1"), Profile());
+        await analyzer.AnalyzeAsync(new IdeaAnalysisInput("Second", "b", null, "S2"), Profile());
+
+        Assert.Equal(2, systems.Count);
+        Assert.Equal(systems[0], systems[1]); // system prompt depends only on the profile, not the input
+        var idxA = systems[0].IndexOf("Example A", StringComparison.Ordinal);
+        var idxB = systems[0].IndexOf("Example B", StringComparison.Ordinal);
+        Assert.True(idxA >= 0 && idxB > idxA); // both anchors present, fixed order
+    }
+
+    [Fact]
+    public async Task AnalyzeAsync_NotJson_ReturnsNull()
+    {
+        var analyzer = Build("not json at all", out _, out _);
+        Assert.Null(await analyzer.AnalyzeAsync(Input(), Profile()));
+    }
+
+    [Fact]
+    public async Task AnalyzeAsync_MissingPillarsArray_ReturnsNull()
+    {
+        var analyzer = Build("""{"isAntiTopic":false,"isAuthorityTopic":false,"reason":"r"}""", out _, out _);
+        Assert.Null(await analyzer.AnalyzeAsync(Input(), Profile()));
     }
 
     [Fact]
     public async Task AnalyzeAsync_FencedJson_StripsFencesAndParses()
     {
-        var analyzer = Build("```json\n{\"score\":3,\"reason\":\"r\",\"summary\":\"s\",\"category\":null,\"tags\":[]}\n```", out _);
-        var result = await analyzer.AnalyzeAsync("T", null, null, "S");
+        var analyzer = Build(
+            "```json\n{\"pillars\":[{\"name\":\"Enterprise AI Adoption\",\"score\":1,\"reason\":\"r\"},{\"name\":\"Agentic Development\",\"score\":0,\"reason\":\"r\"}],\"isAntiTopic\":false,\"isAuthorityTopic\":false,\"reason\":\"r\"}\n```",
+            out _, out _);
+
+        var result = await analyzer.AnalyzeAsync(Input(), Profile());
+
         Assert.NotNull(result);
-        Assert.Equal(3, result!.Score);
+        Assert.Equal(2, result!.Pillars.Count);
     }
 
     [Fact]
-    public async Task AnalyzeAsync_ScoreOutOfRange_ClampsTo0To10()
+    public async Task AnalyzeAsync_AllPillarScoresIdentical_IsRejectedAndWarned()
     {
-        var analyzer = Build("""{"score":15,"reason":"r","summary":"s","category":null,"tags":[]}""", out _);
-        var result = await analyzer.AnalyzeAsync("T", null, null, "S");
-        Assert.Equal(10, result!.Score);
+        var analyzer = Build(
+            """{"pillars":[{"name":"Enterprise AI Adoption","score":0.5,"reason":"r"},{"name":"Agentic Development","score":0.5,"reason":"r"}],"isAntiTopic":false,"isAuthorityTopic":false,"reason":"r"}""",
+            out _, out var logger);
+
+        var result = await analyzer.AnalyzeAsync(Input(), Profile());
+
+        Assert.Null(result); // central-collapse guard (R-M5)
+        VerifyWarning(logger, Times.AtLeastOnce());
+    }
+
+    [Fact]
+    public async Task AnalyzeAsync_PassesLowTemperatureAndCheapModel()
+    {
+        var temps = new List<double?>();
+        var analyzer = Build(
+            """{"pillars":[{"name":"Enterprise AI Adoption","score":0.75,"reason":"r"},{"name":"Agentic Development","score":0.25,"reason":"r"}],"isAntiTopic":false,"isAuthorityTopic":false,"reason":"r"}""",
+            out var sidecar, out _, capturedTemps: temps);
+
+        await analyzer.AnalyzeAsync(Input(), Profile());
+
+        var temp = Assert.Single(temps);
+        Assert.True(temp is >= 0 and <= 0.2, $"temperature {temp} must be in [0, 0.2]");
+        sidecar.Verify(s => s.SendPromptAsync(
+            It.IsAny<string>(), It.IsAny<string>(), "cheap-model", It.IsAny<double?>(),
+            It.IsAny<CancellationToken>()), Times.Once);
     }
 }
diff --git a/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClustererTests.cs b/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClustererTests.cs
deleted file mode 100644
index a2ec833..0000000
--- a/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClustererTests.cs
+++ /dev/null
@@ -1,60 +0,0 @@
-using Microsoft.Extensions.Logging.Abstractions;
-using Microsoft.Extensions.Options;
-using Moq;
-using PBA.Application.Common.Interfaces;
-using PBA.Infrastructure.Configuration;
-using PBA.Infrastructure.Services.Radar;
-using Xunit;
-
-namespace PBA.Infrastructure.Tests.Services.Radar;
-
-public class IdeaClustererTests
-{
-    private static IdeaClusterer Build(string llmResponse)
-    {
-        var sidecar = new Mock<ISidecarClient>();
-        sidecar.Setup(s => s.SendPromptAsync(
-                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
-            .ReturnsAsync(llmResponse);
-        return new IdeaClusterer(sidecar.Object, Options.Create(new ClusteringOptions()),
-            NullLogger<IdeaClusterer>.Instance);
-    }
-
-    private static IReadOnlyList<ClusterInput> Items() =>
-    [
-        new(0, "GPT-5 released", "OpenAI ships GPT-5"),
-        new(1, "OpenAI launches GPT-5", "New flagship model"),
-        new(2, "Unrelated story", "Something else")
-    ];
-
-    [Fact]
-    public async Task ClusterAsync_ValidGroups_ReturnsGroups()
-    {
-        var clusterer = Build("""{"groups":[[0,1]]}""");
-        var groups = await clusterer.ClusterAsync(Items());
-        Assert.Single(groups);
-        Assert.Equal(new[] { 0, 1 }, groups[0]);
-    }
-
-    [Fact]
-    public async Task ClusterAsync_MalformedJson_ReturnsEmpty()
-    {
-        var clusterer = Build("garbage");
-        var groups = await clusterer.ClusterAsync(Items());
-        Assert.Empty(groups);
-    }
-
-    [Fact]
-    public async Task ClusterAsync_FewerThanTwoItems_DoesNotCallLlm()
-    {
-        var sidecar = new Mock<ISidecarClient>();
-        var clusterer = new IdeaClusterer(sidecar.Object, Options.Create(new ClusteringOptions()),
-            NullLogger<IdeaClusterer>.Instance);
-
-        var groups = await clusterer.ClusterAsync([new(0, "only one", null)]);
-
-        Assert.Empty(groups);
-        sidecar.Verify(s => s.SendPromptAsync(
-            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
-    }
-}
diff --git a/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClusteringServiceTests.cs b/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClusteringServiceTests.cs
deleted file mode 100644
index 43cb89f..0000000
--- a/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClusteringServiceTests.cs
+++ /dev/null
@@ -1,81 +0,0 @@
-using Microsoft.EntityFrameworkCore;
-using Microsoft.Extensions.DependencyInjection;
-using Microsoft.Extensions.Logging.Abstractions;
-using Microsoft.Extensions.Options;
-using Moq;
-using PBA.Application.Common.Interfaces;
-using PBA.Domain.Entities;
-using PBA.Domain.Enums;
-using PBA.Infrastructure.Configuration;
-using PBA.Infrastructure.Data;
-using PBA.Infrastructure.Services.Radar;
-using Xunit;
-
-namespace PBA.Infrastructure.Tests.Services.Radar;
-
-public class IdeaClusteringServiceTests
-{
-    private static (IdeaClusteringService svc, ApplicationDbContext db, Mock<IIdeaClusterer> clusterer)
-        Build(ClusteringOptions options)
-    {
-        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
-            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
-
-        var clusterer = new Mock<IIdeaClusterer>();
-
-        // Use mock scope so scope.Dispose() does not dispose the shared db instance.
-        // Follows the same pattern as IdeaScoringServiceTests.
-        var serviceProvider = new Mock<IServiceProvider>();
-        serviceProvider.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
-        serviceProvider.Setup(p => p.GetService(typeof(IIdeaClusterer))).Returns(clusterer.Object);
-
-        var scope = new Mock<IServiceScope>();
-        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
-
-        var scopeFactory = new Mock<IServiceScopeFactory>();
-        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
-
-        var svc = new IdeaClusteringService(scopeFactory.Object, Options.Create(options),
-            NullLogger<IdeaClusteringService>.Instance);
-        return (svc, db, clusterer);
-    }
-
-    private static Idea Scored(int score) => new()
-    {
-        Title = Guid.NewGuid().ToString(), SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
-        Status = IdeaStatus.New, DetectedAt = DateTimeOffset.UtcNow, Score = score,
-        ScoredAt = DateTimeOffset.UtcNow, ClusteredAt = null, DuplicateOfId = null
-    };
-
-    [Fact]
-    public async Task ClusterBatchAsync_GroupsReturned_SetsDuplicateOfIdOnSecondary()
-    {
-        var (svc, db, clusterer) = Build(new ClusteringOptions { MinScore = 6, LookbackHours = 48 });
-        var a = Scored(8); var b = Scored(7);
-        db.Ideas.AddRange(a, b);
-        await db.SaveChangesAsync();
-
-        clusterer.Setup(c => c.ClusterAsync(It.IsAny<IReadOnlyList<ClusterInput>>(), It.IsAny<CancellationToken>()))
-            .ReturnsAsync(new List<IReadOnlyList<int>> { new[] { 0, 1 } });
-
-        await svc.ClusterBatchAsync(CancellationToken.None);
-
-        var primary = db.Ideas.Single(i => i.DuplicateOfId == null);
-        var dup = db.Ideas.Single(i => i.DuplicateOfId != null);
-        Assert.Equal(primary.Id, dup.DuplicateOfId);
-        Assert.All(db.Ideas, i => Assert.NotNull(i.ClusteredAt));
-    }
-
-    [Fact]
-    public async Task ClusterBatchAsync_LowScoreIdeas_AreExcluded()
-    {
-        var (svc, db, clusterer) = Build(new ClusteringOptions { MinScore = 6 });
-        db.Ideas.AddRange(Scored(3), Scored(2));
-        await db.SaveChangesAsync();
-
-        await svc.ClusterBatchAsync(CancellationToken.None);
-
-        clusterer.Verify(c => c.ClusterAsync(It.IsAny<IReadOnlyList<ClusterInput>>(), It.IsAny<CancellationToken>()),
-            Times.Never);
-    }
-}
diff --git a/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaDedupServiceTests.cs b/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaDedupServiceTests.cs
new file mode 100644
index 0000000..0f1cd22
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaDedupServiceTests.cs
@@ -0,0 +1,134 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Options;
+using Moq;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Data;
+using PBA.Infrastructure.Services.Radar;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Services.Radar;
+
+public class IdeaDedupServiceTests
+{
+    private static (IdeaDedupService svc, ApplicationDbContext db) Build(
+        ClusteringOptions options, RankingOptions ranking)
+    {
+        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
+
+        var sp = new Mock<IServiceProvider>();
+        sp.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
+        var scope = new Mock<IServiceScope>();
+        scope.Setup(s => s.ServiceProvider).Returns(sp.Object);
+        var scopeFactory = new Mock<IServiceScopeFactory>();
+        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
+
+        var monitor = Mock.Of<IOptionsMonitor<RankingOptions>>(m => m.CurrentValue == ranking);
+        var svc = new IdeaDedupService(scopeFactory.Object, Options.Create(options), monitor,
+            NullLogger<IdeaDedupService>.Instance);
+        return (svc, db);
+    }
+
+    private static Idea Embedded(float[] embedding, int score = 5) => new()
+    {
+        Title = Guid.NewGuid().ToString(), SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
+        Status = IdeaStatus.New, DetectedAt = DateTimeOffset.UtcNow, Embedding = embedding, Score = score,
+        DuplicateOfId = null, ClusteredAt = null
+    };
+
+    private static ClusteringOptions Opts() => new() { LookbackHours = 48, MaxItemsPerSweep = 40 };
+    private static RankingOptions Ranking(double threshold = 0.85) => new() { DedupThreshold = threshold };
+
+    [Fact]
+    public async Task DedupBatchAsync_SimilarPair_IsGrouped_DissimilarIsNot()
+    {
+        var (svc, db) = Build(Opts(), Ranking());
+        var a = Embedded(new float[] { 1, 0 }, score: 8);
+        var b = Embedded(new float[] { 1, 0 }, score: 5);    // cosine 1 with a -> duplicate
+        var c = Embedded(new float[] { 0, 1 }, score: 7);    // cosine 0 with both -> standalone
+        db.Ideas.AddRange(a, b, c);
+        await db.SaveChangesAsync();
+
+        await svc.DedupBatchAsync(CancellationToken.None);
+
+        Assert.Equal(a.Id, db.Ideas.Single(i => i.Id == b.Id).DuplicateOfId);
+        Assert.Null(db.Ideas.Single(i => i.Id == a.Id).DuplicateOfId);
+        Assert.Null(db.Ideas.Single(i => i.Id == c.Id).DuplicateOfId);
+        Assert.All(db.Ideas, i => Assert.NotNull(i.ClusteredAt));
+    }
+
+    [Fact]
+    public async Task DedupBatchAsync_HighestBrandFit_IsPrimary()
+    {
+        var (svc, db) = Build(Opts(), Ranking());
+        var low = Embedded(new float[] { 1, 0 }, score: 4);
+        var high = Embedded(new float[] { 1, 0 }, score: 9);
+        db.Ideas.AddRange(low, high);
+        await db.SaveChangesAsync();
+
+        await svc.DedupBatchAsync(CancellationToken.None);
+
+        Assert.Equal(high.Id, db.Ideas.Single(i => i.Id == low.Id).DuplicateOfId);
+        Assert.Null(db.Ideas.Single(i => i.Id == high.Id).DuplicateOfId);
+    }
+
+    [Fact]
+    public async Task DedupBatchAsync_AnyInWindowUnembedded_IsGatedAndSetsNoDuplicate()
+    {
+        var (svc, db) = Build(Opts(), Ranking());
+        var a = Embedded(new float[] { 1, 0 });
+        var b = Embedded(new float[] { 1, 0 });
+        var unembedded = new Idea
+        {
+            Title = "U", SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
+            Status = IdeaStatus.New, DetectedAt = DateTimeOffset.UtcNow, Embedding = null
+        };
+        db.Ideas.AddRange(a, b, unembedded);
+        await db.SaveChangesAsync();
+
+        await svc.DedupBatchAsync(CancellationToken.None);
+
+        Assert.All(db.Ideas, i => Assert.Null(i.DuplicateOfId)); // R-H1: gated, no grouping
+        Assert.All(db.Ideas, i => Assert.Null(i.ClusteredAt));
+    }
+
+    [Fact]
+    public async Task DedupBatchAsync_AlreadyDedupedOrClustered_AreNotReGrouped()
+    {
+        var (svc, db) = Build(Opts(), Ranking());
+        var primary = Embedded(new float[] { 1, 0 }, score: 9);
+        var alreadyDup = Embedded(new float[] { 1, 0 });
+        alreadyDup.DuplicateOfId = Guid.NewGuid();
+        var alreadyClustered = Embedded(new float[] { 1, 0 });
+        alreadyClustered.ClusteredAt = DateTimeOffset.UtcNow.AddHours(-1);
+        db.Ideas.AddRange(primary, alreadyDup, alreadyClustered);
+        await db.SaveChangesAsync();
+
+        await svc.DedupBatchAsync(CancellationToken.None);
+
+        // Only `primary` is a candidate; < 2 candidates -> nothing changes.
+        Assert.NotEqual(primary.Id, db.Ideas.Single(i => i.Id == alreadyDup.Id).DuplicateOfId);
+        Assert.Null(db.Ideas.Single(i => i.Id == primary.Id).ClusteredAt);
+    }
+
+    [Fact]
+    public async Task DedupBatchAsync_RespectsLookbackWindow()
+    {
+        var (svc, db) = Build(new ClusteringOptions { LookbackHours = 24, MaxItemsPerSweep = 40 }, Ranking());
+        var inWindow = Embedded(new float[] { 1, 0 }, score: 8);
+        var outOfWindow = Embedded(new float[] { 1, 0 }, score: 5);
+        outOfWindow.DetectedAt = DateTimeOffset.UtcNow.AddHours(-48); // older than lookback
+        db.Ideas.AddRange(inWindow, outOfWindow);
+        await db.SaveChangesAsync();
+
+        await svc.DedupBatchAsync(CancellationToken.None);
+
+        // out-of-window excluded -> only one candidate -> no grouping
+        Assert.Null(db.Ideas.Single(i => i.Id == inWindow.Id).DuplicateOfId);
+        Assert.Null(db.Ideas.Single(i => i.Id == outOfWindow.Id).DuplicateOfId);
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaEmbeddingServiceTests.cs b/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaEmbeddingServiceTests.cs
new file mode 100644
index 0000000..6b3dec0
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaEmbeddingServiceTests.cs
@@ -0,0 +1,168 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Options;
+using Moq;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Data;
+using PBA.Infrastructure.Services.Radar;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Services.Radar;
+
+public class IdeaEmbeddingServiceTests
+{
+    private static (IdeaEmbeddingService svc, ApplicationDbContext db, Mock<ISidecarClient> sidecar)
+        Build(EmbeddingOptions? options = null)
+    {
+        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
+        var sidecar = new Mock<ISidecarClient>();
+        var opts = options ?? new EmbeddingOptions { Model = "embed-model", Dimensions = 1536, BatchSize = 128 };
+        var monitor = Mock.Of<IOptionsMonitor<EmbeddingOptions>>(m => m.CurrentValue == opts);
+        var svc = new IdeaEmbeddingService(db, sidecar.Object, monitor,
+            NullLogger<IdeaEmbeddingService>.Instance);
+        return (svc, db, sidecar);
+    }
+
+    private static Idea NewIdea(string title = "Title", string? description = "Desc", float[]? embedding = null) => new()
+    {
+        Title = title, Description = description, SourceName = "S",
+        DeduplicationKey = Guid.NewGuid().ToString(), Status = IdeaStatus.New,
+        DetectedAt = DateTimeOffset.UtcNow, Embedding = embedding
+    };
+
+    // EmbedAsync stub: one vector per input, so result count always matches input count.
+    private static void SetupEmbed(Mock<ISidecarClient> sidecar, float[] vector) =>
+        sidecar.Setup(s => s.EmbedAsync(
+                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync((IReadOnlyList<string> inputs, string? _, CancellationToken _) =>
+                inputs.Select(_ => vector).ToList());
+
+    private static BrandRankingProfile ActiveProfile(params BrandPillar[] pillars) => new()
+    {
+        Version = 1, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
+        Positioning = "P", AudiencePrimary = "A", Pillars = pillars.ToList()
+    };
+
+    [Fact]
+    public async Task EmbedPendingAsync_NullEmbeddingIdea_EmbedsAndStampsEmbeddedAt()
+    {
+        var (svc, db, sidecar) = Build();
+        SetupEmbed(sidecar, new float[] { 1, 0 });
+        db.Ideas.Add(NewIdea(title: "Title", description: "Desc"));
+        await db.SaveChangesAsync();
+
+        await svc.EmbedPendingAsync(CancellationToken.None);
+
+        var idea = db.Ideas.Single();
+        Assert.Equal(new float[] { 1, 0 }, idea.Embedding);
+        Assert.NotNull(idea.EmbeddedAt);
+        sidecar.Verify(s => s.EmbedAsync(
+            It.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "Title Desc"),
+            "embed-model", It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task EmbedPendingAsync_AlreadyEmbeddedIdea_IsNotReEmbedded()
+    {
+        var (svc, db, sidecar) = Build();
+        db.Ideas.Add(NewIdea(embedding: new float[] { 0.5f, 0.5f }));
+        await db.SaveChangesAsync();
+
+        await svc.EmbedPendingAsync(CancellationToken.None);
+
+        sidecar.Verify(s => s.EmbedAsync(
+            It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
+            Times.Never);
+    }
+
+    [Fact]
+    public async Task EmbedPendingAsync_ActiveProfilePillarsWithNullVector_AreEmbedded()
+    {
+        var (svc, db, sidecar) = Build();
+        SetupEmbed(sidecar, new float[] { 1, 0 });
+        db.BrandRankingProfiles.Add(ActiveProfile(
+            new BrandPillar { Name = "Pillar", Description = "desc", Weight = 1, Order = 0 }));
+        await db.SaveChangesAsync();
+
+        await svc.EmbedPendingAsync(CancellationToken.None);
+
+        var pillar = db.BrandRankingProfiles.Include(p => p.Pillars).Single().Pillars.Single();
+        Assert.Equal(new float[] { 1, 0 }, pillar.DescriptionEmbedding);
+    }
+
+    [Fact]
+    public async Task EmbedPendingAsync_EmbedBatchThrows_LeavesIdeaNullForRetry()
+    {
+        var (svc, db, sidecar) = Build();
+        sidecar.Setup(s => s.EmbedAsync(
+                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .ThrowsAsync(new InvalidOperationException("429"));
+        db.Ideas.Add(NewIdea());
+        await db.SaveChangesAsync();
+
+        await svc.EmbedPendingAsync(CancellationToken.None); // must not throw
+
+        Assert.Null(db.Ideas.Single().Embedding);
+    }
+
+    [Fact]
+    public async Task EmbedPendingAsync_ZeroVector_IsRejectedAndLeavesIdeaNull()
+    {
+        var (svc, db, sidecar) = Build();
+        SetupEmbed(sidecar, new float[] { 0, 0 });
+        db.Ideas.Add(NewIdea());
+        await db.SaveChangesAsync();
+
+        await svc.EmbedPendingAsync(CancellationToken.None);
+
+        Assert.Null(db.Ideas.Single().Embedding);
+    }
+
+    [Fact]
+    public async Task EmbedPendingAsync_NaNVector_IsRejectedAndLeavesIdeaNull()
+    {
+        var (svc, db, sidecar) = Build();
+        SetupEmbed(sidecar, new[] { float.NaN, 1f });
+        db.Ideas.Add(NewIdea());
+        await db.SaveChangesAsync();
+
+        await svc.EmbedPendingAsync(CancellationToken.None);
+
+        Assert.Null(db.Ideas.Single().Embedding);
+    }
+
+    [Fact]
+    public void ComputeEmbeddingBrandFit_WeightsCosinePerPillar()
+    {
+        var (svc, _, _) = Build();
+        var pillars = new List<BrandPillar>
+        {
+            new() { Name = "A", Description = "a", Weight = 0.5, DescriptionEmbedding = new float[] { 1, 0 } },
+            new() { Name = "B", Description = "b", Weight = 0.3, DescriptionEmbedding = new float[] { 0, 1 } },
+        };
+
+        // 0.5 × cos([1,0],[1,0]) + 0.3 × cos([1,0],[0,1]) = 0.5×1 + 0.3×0 = 0.5
+        var fit = svc.ComputeEmbeddingBrandFit(new float[] { 1, 0 }, pillars);
+
+        Assert.Equal(0.5, fit, 6);
+    }
+
+    [Fact]
+    public void ComputeEmbeddingBrandFit_NullPillarEmbedding_IsSkipped()
+    {
+        var (svc, _, _) = Build();
+        var pillars = new List<BrandPillar>
+        {
+            new() { Name = "A", Description = "a", Weight = 0.5, DescriptionEmbedding = new float[] { 1, 0 } },
+            new() { Name = "B", Description = "b", Weight = 0.4, DescriptionEmbedding = null },
+        };
+
+        var fit = svc.ComputeEmbeddingBrandFit(new float[] { 1, 0 }, pillars);
+
+        Assert.Equal(0.5, fit, 6); // the null-embedding pillar contributes nothing, no NaN
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaScoringServiceTests.cs b/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaScoringServiceTests.cs
index 6098ff7..8472140 100644
--- a/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaScoringServiceTests.cs
+++ b/tests/PBA.Infrastructure.Tests/Services/Radar/IdeaScoringServiceTests.cs
@@ -16,103 +16,226 @@ namespace PBA.Infrastructure.Tests.Services.Radar;
 
 public class IdeaScoringServiceTests
 {
-    private static (IdeaScoringService svc, ApplicationDbContext db, Mock<IIdeaAnalyzer> analyzer)
-        Build(IdeaScoringOptions options)
+    private static readonly Guid P1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
+    private static readonly Guid P2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
+
+    // CurrentValue read-counter, to prove the sweep snapshots ranking options exactly once (R-L2).
+    private sealed class CountingMonitor<T>(T value) : IOptionsMonitor<T>
     {
-        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
-            .UseInMemoryDatabase(Guid.NewGuid().ToString())
-            .Options;
-        var db = new ApplicationDbContext(dbOptions);
+        public int Reads { get; private set; }
+        public T CurrentValue { get { Reads++; return value; } }
+        public T Get(string? name) => value;
+        public IDisposable? OnChange(Action<T, string?> listener) => null;
+    }
 
-        var analyzer = new Mock<IIdeaAnalyzer>();
-        analyzer.Setup(a => a.AnalyzeAsync(
-                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
-            .ReturnsAsync(new IdeaAnalysis(7, "reason", "summary", "AI", new[] { "tag1" }));
+    private static BrandRankingProfile Profile(int version = 2) => new()
+    {
+        Version = version, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
+        Positioning = "P", AudiencePrimary = "A",
+        HalfLifeDays = 7, DecayFloor = 0.075, AntiTopicMultiplier = 0.1, AuthorityBoost = 1.2,
+        Pillars =
+        [
+            new BrandPillar { Id = P1, Name = "A", Description = "a", Weight = 0.6, Order = 0,
+                DescriptionEmbedding = new float[] { 1, 0 } },
+            new BrandPillar { Id = P2, Name = "B", Description = "b", Weight = 0.4, Order = 1,
+                DescriptionEmbedding = new float[] { 0, 1 } },
+        ]
+    };
+
+    private static Idea NewIdea(float[]? embedding, DateTimeOffset? detectedAt = null,
+        int? scoredVersion = null, int attempts = 0) => new()
+    {
+        Title = "T", Description = "D", SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
+        Status = IdeaStatus.New, DetectedAt = detectedAt ?? DateTimeOffset.UtcNow,
+        Embedding = embedding, ScoredProfileVersion = scoredVersion, ScoreAttempts = attempts
+    };
 
-        // Use mock scope so scope.Dispose() does not dispose the shared db instance.
-        // Follows the same pattern as SourcePollingServiceTests.
-        var serviceProvider = new Mock<IServiceProvider>();
-        serviceProvider.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
-        serviceProvider.Setup(p => p.GetService(typeof(IIdeaAnalyzer))).Returns(analyzer.Object);
+    private static (IdeaScoringService svc, ApplicationDbContext db, Mock<IIdeaAnalyzer> analyzer) Build(
+        IdeaScoringOptions options, RankingOptions ranking, IOptionsMonitor<RankingOptions>? rankingMonitor = null)
+    {
+        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
 
+        var analyzer = new Mock<IIdeaAnalyzer>();
+        analyzer.Setup(a => a.AnalyzeAsync(
+                It.IsAny<IdeaAnalysisInput>(), It.IsAny<BrandRankingProfileSnapshot>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new IdeaAnalysis(
+                [new PillarScore(P1, "A", 0.8, "r"), new PillarScore(P2, "B", 0.2, "r")],
+                IsAntiTopic: false, IsAuthorityTopic: true, Reason: "reason"));
+
+        // Embedder is real (shares the in-memory db) but its sidecar returns nothing, so EmbedPendingAsync
+        // never silently embeds a test idea — candidate sets stay determined by the seeded Embedding values.
+        var embedSidecar = new Mock<ISidecarClient>();
+        embedSidecar.Setup(s => s.EmbedAsync(
+                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync((IReadOnlyList<float[]>)new List<float[]>());
+        var embedder = new IdeaEmbeddingService(db, embedSidecar.Object,
+            Mock.Of<IOptionsMonitor<EmbeddingOptions>>(m => m.CurrentValue == new EmbeddingOptions()),
+            NullLogger<IdeaEmbeddingService>.Instance);
+
+        var sp = new Mock<IServiceProvider>();
+        sp.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
+        sp.Setup(p => p.GetService(typeof(IIdeaAnalyzer))).Returns(analyzer.Object);
+        sp.Setup(p => p.GetService(typeof(IdeaEmbeddingService))).Returns(embedder);
         var scope = new Mock<IServiceScope>();
-        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
-
+        scope.Setup(s => s.ServiceProvider).Returns(sp.Object);
         var scopeFactory = new Mock<IServiceScopeFactory>();
         scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
 
-        var svc = new IdeaScoringService(scopeFactory.Object, Options.Create(options),
+        var monitor = rankingMonitor ?? Mock.Of<IOptionsMonitor<RankingOptions>>(m => m.CurrentValue == ranking);
+        var svc = new IdeaScoringService(scopeFactory.Object, Options.Create(options), monitor,
             NullLogger<IdeaScoringService>.Instance);
         return (svc, db, analyzer);
     }
 
-    private static Idea NewIdea(DateTimeOffset detectedAt) => new()
+    private static void VerifyAnalyzed(Mock<IIdeaAnalyzer> analyzer, Times times) =>
+        analyzer.Verify(a => a.AnalyzeAsync(
+            It.IsAny<IdeaAnalysisInput>(), It.IsAny<BrandRankingProfileSnapshot>(), It.IsAny<CancellationToken>()),
+            times);
+
+    private static RankingOptions Ranking() => new() { PreFilterThreshold = 0.5, ScoringWindowDays = 30 };
+
+    [Fact]
+    public async Task ScoreSweepAsync_CandidateSet_ExcludesOutOfWindowUnembeddedAndCurrentVersion()
     {
-        Title = "T", SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
-        Status = IdeaStatus.New, DetectedAt = detectedAt, ScoredAt = null
-    };
+        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 }, Ranking());
+        db.BrandRankingProfiles.Add(Profile(version: 2));
+        var valid = NewIdea(new float[] { 1, 0 });
+        var old = NewIdea(new float[] { 1, 0 }, detectedAt: DateTimeOffset.UtcNow.AddDays(-40));
+        var unembedded = NewIdea(null);
+        var current = NewIdea(new float[] { 1, 0 }, scoredVersion: 2);
+        db.Ideas.AddRange(valid, old, unembedded, current);
+        await db.SaveChangesAsync();
+
+        await svc.ScoreSweepAsync(CancellationToken.None);
+
+        VerifyAnalyzed(analyzer, Times.Once());
+        Assert.Equal(2, db.Ideas.Single(i => i.Id == valid.Id).ScoredProfileVersion);
+        Assert.Null(db.Ideas.Single(i => i.Id == old.Id).ScoredProfileVersion);
+        Assert.Null(db.Ideas.Single(i => i.Id == unembedded.Id).ScoredProfileVersion);
+    }
+
+    [Fact]
+    public async Task ScoreSweepAsync_SnapshotsRankingOptionsOnce()
+    {
+        var counting = new CountingMonitor<RankingOptions>(Ranking());
+        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 },
+            new RankingOptions(), rankingMonitor: counting);
+        db.BrandRankingProfiles.Add(Profile());
+        db.Ideas.Add(NewIdea(new float[] { 1, 0 }));
+        await db.SaveChangesAsync();
+
+        await svc.ScoreSweepAsync(CancellationToken.None);
+
+        Assert.Equal(1, counting.Reads); // R-L2: snapshot once per sweep
+    }
 
     [Fact]
-    public async Task ScoreBatchAsync_UnscoredIdea_PopulatesScoreSummaryTags()
+    public async Task ScoreSweepAsync_AboveThreshold_StoresSubScoresFlagsVersionAndDerivedScore()
     {
-        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 });
-        db.Ideas.Add(NewIdea(DateTimeOffset.UtcNow));
+        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 }, Ranking());
+        db.BrandRankingProfiles.Add(Profile(version: 2));
+        db.Ideas.Add(NewIdea(new float[] { 1, 0 })); // fit = 0.6 >= 0.5
         await db.SaveChangesAsync();
 
-        await svc.ScoreBatchAsync(backfillCutoff: null, CancellationToken.None);
+        await svc.ScoreSweepAsync(CancellationToken.None);
 
         var idea = db.Ideas.Single();
-        Assert.Equal(7, idea.Score);
-        Assert.Equal("summary", idea.Summary);
-        Assert.Equal("AI", idea.Category);
-        Assert.Equal(new[] { "tag1" }, idea.Tags);
+        Assert.Equal(2, idea.PillarSubScores.Count);
+        Assert.Contains(idea.PillarSubScores, s => s.PillarId == P1 && Math.Abs(s.Score - 0.8) < 1e-9);
+        Assert.True(idea.IsAuthorityTopic);
+        Assert.False(idea.IsAntiTopic);
+        Assert.Equal("reason", idea.ScoreReason);
+        Assert.Equal(2, idea.ScoredProfileVersion);
         Assert.NotNull(idea.ScoredAt);
+        // RenormalizedSubScore = (0.6×0.8 + 0.4×0.2) / 1.0 = 0.56 -> round(5.6) = 6
+        Assert.Equal(6, idea.Score);
     }
 
     [Fact]
-    public async Task ScoreBatchAsync_BackfillCutoffSet_SkipsOldIdeas()
+    public async Task ScoreSweepAsync_BelowThreshold_NoLlmCall_StampsVersion_EmbeddingDerivedScore()
     {
-        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 });
-        var cutoff = DateTimeOffset.UtcNow;
-        db.Ideas.Add(NewIdea(cutoff.AddDays(-5)));   // old, should be skipped
-        db.Ideas.Add(NewIdea(cutoff.AddMinutes(5))); // new, should be scored
+        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 }, Ranking());
+        db.BrandRankingProfiles.Add(Profile(version: 2));
+        db.Ideas.Add(NewIdea(new float[] { 0, 1 })); // fit = 0.4 < 0.5
         await db.SaveChangesAsync();
 
-        await svc.ScoreBatchAsync(backfillCutoff: cutoff, CancellationToken.None);
+        await svc.ScoreSweepAsync(CancellationToken.None);
 
-        Assert.Equal(1, db.Ideas.Count(i => i.ScoredAt != null));
-        analyzer.Verify(a => a.AnalyzeAsync(
-            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
-            Times.Once);
+        VerifyAnalyzed(analyzer, Times.Never());
+        var idea = db.Ideas.Single();
+        Assert.Empty(idea.PillarSubScores);
+        Assert.Equal(2, idea.ScoredProfileVersion);
+        Assert.Equal(4, idea.Score); // round(0.4 * 10)
     }
 
     [Fact]
-    public async Task ScoreBatchAsync_RespectsBatchSize()
+    public async Task ScoreSweepAsync_ScoreAttemptsAtCap_IsSkipped()
     {
-        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 2, ThrottleMs = 0 });
-        for (var i = 0; i < 5; i++) db.Ideas.Add(NewIdea(DateTimeOffset.UtcNow));
+        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 }, Ranking());
+        db.BrandRankingProfiles.Add(Profile());
+        db.Ideas.Add(NewIdea(new float[] { 1, 0 }, attempts: 3));
         await db.SaveChangesAsync();
 
-        await svc.ScoreBatchAsync(backfillCutoff: null, CancellationToken.None);
+        await svc.ScoreSweepAsync(CancellationToken.None);
 
-        Assert.Equal(2, db.Ideas.Count(i => i.ScoredAt != null));
-        analyzer.Verify(a => a.AnalyzeAsync(
-            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
-            Times.Exactly(2));
+        VerifyAnalyzed(analyzer, Times.Never());
     }
 
     [Fact]
-    public async Task ScoreBatchAsync_AnalyzerReturnsNull_LeavesIdeaUnscored()
+    public async Task ScoreSweepAsync_AnalyzerReturnsNull_IncrementsAttempts_NoVersionStamp()
     {
-        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 });
+        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 }, Ranking());
         analyzer.Setup(a => a.AnalyzeAsync(
-                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+                It.IsAny<IdeaAnalysisInput>(), It.IsAny<BrandRankingProfileSnapshot>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((IdeaAnalysis?)null);
-        db.Ideas.Add(NewIdea(DateTimeOffset.UtcNow));
+        db.BrandRankingProfiles.Add(Profile());
+        db.Ideas.Add(NewIdea(new float[] { 1, 0 }));
+        await db.SaveChangesAsync();
+
+        await svc.ScoreSweepAsync(CancellationToken.None);
+
+        var idea = db.Ideas.Single();
+        Assert.Equal(1, idea.ScoreAttempts);
+        Assert.Null(idea.ScoredProfileVersion);
+    }
+
+    [Fact]
+    public async Task ScoreSweepAsync_BatchSize_BoundsLlmScoredItems()
+    {
+        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 2, ThrottleMs = 0 }, Ranking());
+        db.BrandRankingProfiles.Add(Profile(version: 2));
+        for (var i = 0; i < 5; i++) db.Ideas.Add(NewIdea(new float[] { 1, 0 })); // all above threshold
+        await db.SaveChangesAsync();
+
+        await svc.ScoreSweepAsync(CancellationToken.None);
+
+        VerifyAnalyzed(analyzer, Times.Exactly(2));
+        Assert.Equal(2, db.Ideas.Count(i => i.ScoredProfileVersion == 2));
+    }
+
+    [Fact]
+    public async Task ScoreSweepAsync_NoCandidates_NoLlmCall_NoThrow()
+    {
+        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 }, Ranking());
+        db.BrandRankingProfiles.Add(Profile());
+        await db.SaveChangesAsync();
+
+        await svc.ScoreSweepAsync(CancellationToken.None);
+
+        VerifyAnalyzed(analyzer, Times.Never());
+    }
+
+    [Fact]
+    public async Task ScoreSweepAsync_ThrottleConfigured_StillScores()
+    {
+        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 1 }, Ranking());
+        db.BrandRankingProfiles.Add(Profile(version: 2));
+        db.Ideas.Add(NewIdea(new float[] { 1, 0 }));
         await db.SaveChangesAsync();
 
-        await svc.ScoreBatchAsync(backfillCutoff: null, CancellationToken.None);
+        await svc.ScoreSweepAsync(CancellationToken.None);
 
-        Assert.Null(db.Ideas.Single().ScoredAt);
+        Assert.Equal(2, db.Ideas.Single().ScoredProfileVersion);
     }
 }
