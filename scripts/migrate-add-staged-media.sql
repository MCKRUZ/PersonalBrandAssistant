-- Staged media for posts PBA holds until their slot.
--
-- Instagram cannot schedule (Meta's Content Publishing API has no such concept), so PBA keeps the
-- post AND the video until the moment of posting. The clip arrives with the request and would
-- otherwise be gone days later, so it is staged on R2 and pointed at from here.
--
-- Purely additive and nullable: existing rows mean "nothing staged", which is correct for every
-- post published immediately and for TikTok, where Buffer holds the clip on our behalf.
START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806014634_AddStagedMediaToContent') THEN
    ALTER TABLE "Contents" ADD "StagedMediaKey" text;
    ALTER TABLE "Contents" ADD "StagedMediaUrl" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806014634_AddStagedMediaToContent') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260806014634_AddStagedMediaToContent', '10.0.7');
    END IF;
END $EF$;
COMMIT;
