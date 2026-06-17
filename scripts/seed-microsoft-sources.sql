-- One-off seed: Microsoft-owned RSS sources for the "Microsoft" daily brief.
--
-- WHY THIS EXISTS: IdeaSource seeding only runs via POST /api/idea-sources/seed, which is registered
-- in Development only. On the production Mac Mini host there is no seed endpoint and seeding does not
-- run at startup, so these rows must be inserted directly once. Mirrors the Microsoft entries in
-- IdeaSourceSeedService.cs exactly (Type=RSS=0, Category='Microsoft', poll 60m, enabled).
--
-- Idempotent: re-running inserts nothing already present (matched case-insensitively on FeedUrl, the
-- same key IdeaSourceSeedService dedups on). Safe to run before or after a code deploy.
--
-- Apply on the Mac Mini, e.g.:
--   docker compose exec -T db psql -U <user> -d <database> -f - < scripts/seed-microsoft-sources.sql
-- (or pipe the file into psql however the other migrations are applied on that host).

INSERT INTO "IdeaSources"
    ("Id", "Name", "Type", "FeedUrl", "ApiUrl", "Category",
     "PollIntervalMinutes", "IsEnabled", "LastPolledAt", "LastSuccessAt", "LastError", "ConsecutiveFailures")
SELECT
    gen_random_uuid(), v.name, 0, v.feedurl, NULL, 'Microsoft',
    60, TRUE, NULL, NULL, NULL, 0
FROM (VALUES
    ('Microsoft Blog',      'https://blogs.microsoft.com/feed/'),
    ('Azure Blog',          'https://azure.microsoft.com/en-us/blog/feed/'),
    ('Microsoft Dev Blogs', 'https://devblogs.microsoft.com/feed/'),
    ('.NET Blog',           'https://devblogs.microsoft.com/dotnet/feed/'),
    ('Visual Studio Blog',  'https://devblogs.microsoft.com/visualstudio/feed/'),
    ('Microsoft Research',  'https://www.microsoft.com/en-us/research/feed/'),
    ('GitHub Blog',         'https://github.blog/feed/')
) AS v(name, feedurl)
WHERE NOT EXISTS (
    SELECT 1 FROM "IdeaSources" s WHERE lower(s."FeedUrl") = lower(v.feedurl)
);
