-- Which frame becomes a post's cover, in milliseconds from the start of the clip.
--
-- These are chosen per clip by whoever cut it (2334ms on one beat, 37290ms on another) — a
-- connector guessing from the video's duration lands on a different, usually worse, moment. The
-- value has to live on the record because a post PBA holds until its slot is published days later,
-- by which time nothing else remembers the choice.
--
-- Nullable and additive: existing rows mean "no choice made", and the connector guesses as before.
START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806022303_AddCoverFrameOffsetToContent') THEN
    ALTER TABLE "Contents" ADD "CoverFrameOffsetMs" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806022303_AddCoverFrameOffsetToContent') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260806022303_AddCoverFrameOffsetToContent', '10.0.7');
    END IF;
END $EF$;
COMMIT;
