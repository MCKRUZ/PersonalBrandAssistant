-- When PBA hands a clip to the platform, which is not always when the post goes live.
--
-- TikTok via Buffer is handed over at once and this stays null. Instagram cannot schedule, so the
-- handover IS the go-live moment and the two are equal. YouTube schedules the release itself but
-- rations uploads to a daily quota, so PBA uploads as early as the budget allows — days BEFORE the
-- video appears. Booking the Hangfire job at the go-live time instead would upload the video after
-- the moment it was supposed to be public, which YouTube rejects outright.
--
-- Recorded rather than read back out of the job store because the pacing decision counts what is
-- already booked onto each day, and a second source of truth for that would drift the first time an
-- upload failed and was retried.
--
-- Nullable and additive: existing rows mean "handed over immediately", which is what they were.
START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806151825_AddHandoverAtToContent') THEN
    ALTER TABLE "Contents" ADD "HandoverAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806151825_AddHandoverAtToContent') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260806151825_AddHandoverAtToContent', '10.0.7');
    END IF;
END $EF$;
COMMIT;
