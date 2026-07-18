START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260717191429_AddChannelAnalytics') THEN

    -- 1. Drop the old single-column unique index so a re-apply never leaves both indexes side by side.
    DROP INDEX IF EXISTS "IX_PlatformCredentials_Platform";

    -- 2. Purpose discriminator on existing credentials (0 = Publishing, back-compat for every existing row).
    ALTER TABLE "PlatformCredentials" ADD COLUMN IF NOT EXISTS "Purpose" integer NOT NULL DEFAULT 0;

    -- 3. New composite filtered unique index: one active credential per (Platform, Purpose).
    CREATE UNIQUE INDEX IF NOT EXISTS "IX_PlatformCredentials_Platform_Purpose"
        ON "PlatformCredentials" ("Platform", "Purpose")
        WHERE "IsActive" = true;

    -- 4. Cumulative daily metric snapshots.
    CREATE TABLE IF NOT EXISTS "ChannelMetricSnapshots" (
        "Id" uuid NOT NULL,
        "Platform" integer NOT NULL,
        "SnapshotDate" date NOT NULL,
        "Scope" integer NOT NULL,
        "VideoId" character varying(128) NOT NULL,
        "VideoTitle" character varying(500) NULL,
        "Metrics" jsonb NOT NULL,
        "CapturedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ChannelMetricSnapshots" PRIMARY KEY ("Id")
    );

    -- 5a. Unique index: one Account row per platform-day (VideoId sentinel "") and one row per video-day.
    CREATE UNIQUE INDEX IF NOT EXISTS "IX_ChannelMetricSnapshots_Platform_SnapshotDate_Scope_VideoId"
        ON "ChannelMetricSnapshots" ("Platform", "SnapshotDate", "Scope", "VideoId");

    -- 5b. Range index for trend/range queries.
    CREATE INDEX IF NOT EXISTS "IX_ChannelMetricSnapshots_Platform_SnapshotDate"
        ON "ChannelMetricSnapshots" ("Platform", "SnapshotDate");

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260717191429_AddChannelAnalytics') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260717191429_AddChannelAnalytics', '10.0.7');
    END IF;
END $EF$;
COMMIT;
