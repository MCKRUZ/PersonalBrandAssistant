-- Ensure the Microsoft-owned RSS sources exist and are flagged for the "Microsoft" daily brief.
--
-- WHY THIS EXISTS: IdeaSource seeding only runs via POST /api/idea-sources/seed (Development only), so on
-- the production Mac Mini host these rows are managed by hand. The Microsoft brief filters on the
-- IsMicrosoftSource flag (NOT on Category, which stays topical: Azure/Cloud, .NET/C#, etc.).
--
-- Run AFTER the AddIsMicrosoftSource migration (the flag column must exist). Idempotent: sets the flag on
-- existing sources (matched case-insensitively on FeedUrl) and inserts any that are missing.
--
-- Apply on the Mac Mini, e.g.:
--   docker exec -i pba-db psql -U pba -d personal_brand_assistant -v ON_ERROR_STOP=1 < scripts/seed-microsoft-sources.sql

-- 1) Flag any already-present Microsoft sources (they may carry topical categories like Azure/Cloud).
UPDATE "IdeaSources" SET "IsMicrosoftSource" = TRUE
WHERE lower("FeedUrl") IN (
    'https://blogs.microsoft.com/feed/',
    'https://azure.microsoft.com/en-us/blog/feed/',
    'https://devblogs.microsoft.com/feed/',
    'https://devblogs.microsoft.com/dotnet/feed/',
    'https://devblogs.microsoft.com/visualstudio/feed/',
    'https://www.microsoft.com/en-us/research/feed/',
    'https://github.blog/feed/'
);

-- 2) Insert any that are missing, flagged and with a sensible topical category.
INSERT INTO "IdeaSources"
    ("Id", "Name", "Type", "FeedUrl", "ApiUrl", "Category", "IsMicrosoftSource",
     "PollIntervalMinutes", "IsEnabled", "LastPolledAt", "LastSuccessAt", "LastError", "ConsecutiveFailures")
SELECT
    gen_random_uuid(), v.name, 0, v.feedurl, NULL, v.category, TRUE,
    60, TRUE, NULL, NULL, NULL, 0
FROM (VALUES
    ('Microsoft Blog',     'https://blogs.microsoft.com/feed/',                'Microsoft'),
    ('Azure Blog',         'https://azure.microsoft.com/en-us/blog/feed/',     'Azure/Cloud'),
    ('Microsoft DevBlogs', 'https://devblogs.microsoft.com/feed/',             'Microsoft'),
    ('.NET Blog',          'https://devblogs.microsoft.com/dotnet/feed/',      '.NET/C#'),
    ('Visual Studio Blog', 'https://devblogs.microsoft.com/visualstudio/feed/','.NET/C#'),
    ('Microsoft Research', 'https://www.microsoft.com/en-us/research/feed/',   'Microsoft'),
    ('GitHub Blog',        'https://github.blog/feed/',                        'DevOps')
) AS v(name, feedurl, category)
WHERE NOT EXISTS (
    SELECT 1 FROM "IdeaSources" s WHERE lower(s."FeedUrl") = lower(v.feedurl)
);
