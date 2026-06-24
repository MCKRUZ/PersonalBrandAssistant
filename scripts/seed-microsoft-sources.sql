-- Ensure Microsoft-owned RSS sources are flagged for the "Microsoft" daily brief.
--
-- WHY THIS EXISTS: IdeaSource seeding only runs via POST /api/idea-sources/seed (Development only), so on
-- the production Mac Mini host these rows are managed by hand. The Microsoft brief filters on the
-- IsMicrosoftSource flag (NOT on Category, which stays topical: Azure/Cloud, .NET/C#, Security, etc.).
--
-- The flag is set by DOMAIN, not a hardcoded feed list: any source on a Microsoft-owned host
-- (*.microsoft.com or github.blog) is Microsoft. This catches the full set (Azure, .NET, M365, Security,
-- PowerShell, TypeScript, Semantic Kernel, DevBlogs, Research, GitHub blog, ...) including feeds imported
-- outside the seed service. The ^-anchor prevents matching lookalikes like notmicrosoft.com, and github.blog
-- (Microsoft's blog) is included while github.com/<third-party> repos are not.
--
-- Run AFTER the AddIsMicrosoftSource migration. Idempotent. Apply on the Mac Mini, e.g.:
--   docker exec -i pba-db psql -U pba -d personal_brand_assistant -v ON_ERROR_STOP=1 < scripts/seed-microsoft-sources.sql

-- 1) Flag every source on a Microsoft-owned domain.
UPDATE "IdeaSources" SET "IsMicrosoftSource" = TRUE
WHERE "FeedUrl" ~* '^https?://([a-z0-9-]+\.)*microsoft\.com/'
   OR "FeedUrl" ~* '^https?://([a-z0-9-]+\.)*github\.blog/';

-- 2) Insert the canonical Microsoft feeds if missing (for hosts without the News Hub source set),
--    flagged and with a sensible topical category.
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
