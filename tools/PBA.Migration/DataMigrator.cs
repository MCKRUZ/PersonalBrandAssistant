using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

namespace PBA.Migration;

public record MigrationResult(int SourcesMigrated, int IdeasMigrated, int SavedIdeasMigrated, int Errors);

public partial class DataMigrator
{
    private readonly string _v1ConnectionString;
    private readonly string _v2ConnectionString;
    private readonly Action<string> _log;

    public DataMigrator(string v1ConnectionString, string v2ConnectionString, Action<string>? log = null)
    {
        _v1ConnectionString = v1ConnectionString;
        _v2ConnectionString = v2ConnectionString;
        _log = log ?? Console.WriteLine;
    }

    public async Task<MigrationResult> MigrateAsync(CancellationToken ct = default)
    {
        await using var v1Conn = new NpgsqlConnection(_v1ConnectionString);
        await using var v2Conn = new NpgsqlConnection(_v2ConnectionString);
        await v1Conn.OpenAsync(ct);
        await v2Conn.OpenAsync(ct);

        await using var tx = await v2Conn.BeginTransactionAsync(ct);
        var errors = 0;

        try
        {
            var sourceMapping = await MigrateSourcesAsync(v1Conn, v2Conn, ct);
            _log($"Phase 1 complete: {sourceMapping.Count} sources migrated");

            var (itemMapping, ideaCount, itemErrors) = await MigrateItemsAsync(v1Conn, v2Conn, sourceMapping, ct);
            errors += itemErrors;
            _log($"Phase 2 complete: {ideaCount} ideas migrated ({itemErrors} errors)");

            var (savedCount, savedErrors) = await MigrateSavedItemsAsync(v1Conn, v2Conn, itemMapping, ct);
            errors += savedErrors;
            _log($"Phase 3 complete: {savedCount} saved ideas migrated ({savedErrors} errors)");

            await tx.CommitAsync(ct);
            return new MigrationResult(sourceMapping.Count, ideaCount, savedCount, errors);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<Dictionary<long, Guid>> MigrateSourcesAsync(
        NpgsqlConnection v1, NpgsqlConnection v2, CancellationToken ct)
    {
        var mapping = new Dictionary<long, Guid>();
        const string query = """
            SELECT id, name, feed_url, poll_interval, is_enabled,
                   last_polled_at, last_success_at, last_error, consecutive_failures, category
            FROM trend_sources
            """;

        await using var cmd = new NpgsqlCommand(query, v1);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var oldId = reader.GetInt64(0);
            var newId = Guid.NewGuid();

            await using var insertCmd = new NpgsqlCommand("""
                INSERT INTO idea_sources ("Id", "Name", "Type", "FeedUrl", "ApiUrl", "Category",
                    "PollIntervalMinutes", "IsEnabled", "LastPolledAt", "LastSuccessAt",
                    "LastError", "ConsecutiveFailures")
                VALUES (@id, @name, @type, @feedUrl, NULL, @category,
                    @pollInterval, @isEnabled, @lastPolledAt, @lastSuccessAt,
                    @lastError, @consecutiveFailures)
                """, v2);

            insertCmd.Parameters.AddWithValue("id", newId);
            insertCmd.Parameters.AddWithValue("name", reader.GetString(1));
            insertCmd.Parameters.AddWithValue("type", 0); // RSS
            insertCmd.Parameters.AddWithValue("feedUrl", GetStringOrDbNull(reader, 2));
            insertCmd.Parameters.AddWithValue("category", GetStringOrDbNull(reader, 9));
            insertCmd.Parameters.AddWithValue("pollInterval", reader.IsDBNull(3) ? 30 : reader.GetInt32(3));
            insertCmd.Parameters.AddWithValue("isEnabled", reader.IsDBNull(4) || reader.GetBoolean(4));
            insertCmd.Parameters.AddWithValue("lastPolledAt", GetTimestampOrDbNull(reader, 5));
            insertCmd.Parameters.AddWithValue("lastSuccessAt", GetTimestampOrDbNull(reader, 6));
            insertCmd.Parameters.AddWithValue("lastError", GetStringOrDbNull(reader, 7));
            insertCmd.Parameters.AddWithValue("consecutiveFailures", reader.IsDBNull(8) ? 0 : reader.GetInt32(8));

            await insertCmd.ExecuteNonQueryAsync(ct);
            mapping[oldId] = newId;
        }

        return mapping;
    }

    private async Task<(Dictionary<long, Guid> Mapping, int Count, int Errors)> MigrateItemsAsync(
        NpgsqlConnection v1, NpgsqlConnection v2,
        Dictionary<long, Guid> sourceMapping, CancellationToken ct)
    {
        var mapping = new Dictionary<long, Guid>();
        var errors = 0;
        const string query = """
            SELECT id, title, description, url, source_name, thumbnail_url,
                   category, tags, detected_at, source_id
            FROM trend_items
            """;

        await using var cmd = new NpgsqlCommand(query, v1);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            try
            {
                var oldId = reader.GetInt64(0);
                var newId = Guid.NewGuid();
                var title = reader.GetString(1);
                var url = reader.IsDBNull(3) ? null : reader.GetString(3);
                var dedupKey = GenerateDeduplicationKey(url, title);
                Guid? ideaSourceId = null;
                if (!reader.IsDBNull(9))
                {
                    var oldSourceId = reader.GetInt64(9);
                    ideaSourceId = sourceMapping.GetValueOrDefault(oldSourceId);
                }

                var tags = ReadTags(reader, 7);

                await using var insertCmd = new NpgsqlCommand("""
                    INSERT INTO ideas ("Id", "Title", "Description", "Url", "SourceName",
                        "IdeaSourceId", "ThumbnailUrl", "Category", "Summary", "AIConnections",
                        "Status", "Tags", "DetectedAt", "DeduplicationKey")
                    VALUES (@id, @title, @description, @url, @sourceName,
                        @ideaSourceId, @thumbnailUrl, @category, NULL, NULL,
                        @status, @tags, @detectedAt, @dedupKey)
                    """, v2);

                insertCmd.Parameters.AddWithValue("id", newId);
                insertCmd.Parameters.AddWithValue("title", title);
                insertCmd.Parameters.AddWithValue("description", GetStringOrDbNull(reader, 2));
                insertCmd.Parameters.AddWithValue("url", (object?)url ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("sourceName", reader.IsDBNull(4) ? "" : reader.GetString(4));
                insertCmd.Parameters.AddWithValue("ideaSourceId", (object?)ideaSourceId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("thumbnailUrl", GetStringOrDbNull(reader, 5));
                insertCmd.Parameters.AddWithValue("category", GetStringOrDbNull(reader, 6));
                insertCmd.Parameters.AddWithValue("status", 0); // New
                insertCmd.Parameters.Add(new NpgsqlParameter("tags", NpgsqlDbType.Jsonb)
                    { Value = JsonSerializer.Serialize(tags) });
                insertCmd.Parameters.AddWithValue("detectedAt",
                    reader.IsDBNull(8) ? DateTimeOffset.UtcNow : reader.GetFieldValue<DateTimeOffset>(8));
                insertCmd.Parameters.AddWithValue("dedupKey", dedupKey);

                await insertCmd.ExecuteNonQueryAsync(ct);
                mapping[oldId] = newId;
            }
            catch (Exception ex)
            {
                errors++;
                _log($"  Error migrating trend_item: {ex.Message}");
            }
        }

        return (mapping, mapping.Count, errors);
    }

    private async Task<(int Count, int Errors)> MigrateSavedItemsAsync(
        NpgsqlConnection v1, NpgsqlConnection v2,
        Dictionary<long, Guid> itemMapping, CancellationToken ct)
    {
        var count = 0;
        var errors = 0;
        var savedIdeaIds = new List<Guid>();
        const string query = "SELECT id, trend_item_id, notes, tags, saved_at FROM saved_trend_items";

        await using var cmd = new NpgsqlCommand(query, v1);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            try
            {
                var oldItemId = reader.GetInt64(1);
                if (!itemMapping.TryGetValue(oldItemId, out var ideaId))
                {
                    _log($"  Warning: saved item references unknown trend_item_id {oldItemId}, skipping");
                    errors++;
                    continue;
                }

                var newId = Guid.NewGuid();
                var tags = ReadTags(reader, 3);

                await using var insertCmd = new NpgsqlCommand("""
                    INSERT INTO saved_ideas ("Id", "IdeaId", "SavedAt", "Notes", "Tags",
                        "SuggestedPlatforms", "SuggestedAngle")
                    VALUES (@id, @ideaId, @savedAt, @notes, @tags, '[]'::jsonb, NULL)
                    """, v2);

                insertCmd.Parameters.AddWithValue("id", newId);
                insertCmd.Parameters.AddWithValue("ideaId", ideaId);
                insertCmd.Parameters.AddWithValue("savedAt",
                    reader.IsDBNull(4) ? DateTimeOffset.UtcNow : reader.GetFieldValue<DateTimeOffset>(4));
                insertCmd.Parameters.AddWithValue("notes", GetStringOrDbNull(reader, 2));
                insertCmd.Parameters.Add(new NpgsqlParameter("tags", NpgsqlDbType.Jsonb)
                    { Value = JsonSerializer.Serialize(tags) });

                await insertCmd.ExecuteNonQueryAsync(ct);
                savedIdeaIds.Add(ideaId);
                count++;
            }
            catch (Exception ex)
            {
                errors++;
                _log($"  Error migrating saved_trend_item: {ex.Message}");
            }
        }

        if (savedIdeaIds.Count > 0)
        {
            await using var updateCmd = new NpgsqlCommand(
                """UPDATE ideas SET "Status" = 1 WHERE "Id" = ANY(@ids)""", v2);
            updateCmd.Parameters.AddWithValue("ids", savedIdeaIds.ToArray());
            await updateCmd.ExecuteNonQueryAsync(ct);
        }

        return (count, errors);
    }

    internal static string GenerateDeduplicationKey(string? url, string title)
    {
        var input = !string.IsNullOrWhiteSpace(url)
            ? NormalizeUrl(url)
            : title.Trim().ToLowerInvariant();
        return ComputeSha256(input);
    }

    internal static string NormalizeUrl(string url)
    {
        var normalized = url.Trim().ToLowerInvariant().TrimEnd('/');
        normalized = UtmParamRegex().Replace(normalized, "");
        normalized = normalized.TrimEnd('?').TrimEnd('&');
        return normalized;
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    private static List<string> ReadTags(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return [];

        try
        {
            var fieldType = reader.GetFieldType(ordinal);
            if (fieldType == typeof(string[]))
                return [.. reader.GetFieldValue<string[]>(ordinal)];

            var raw = reader.GetString(ordinal);
            if (raw.StartsWith('['))
                return JsonSerializer.Deserialize<List<string>>(raw) ?? [];

            return [raw];
        }
        catch
        {
            return [];
        }
    }

    private static object GetStringOrDbNull(NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? DBNull.Value : reader.GetString(ordinal);

    private static object GetTimestampOrDbNull(NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? DBNull.Value : reader.GetFieldValue<DateTimeOffset>(ordinal);

    [GeneratedRegex(@"[?&]utm_\w+=[^&]*", RegexOptions.Compiled)]
    private static partial Regex UtmParamRegex();
}
