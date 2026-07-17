START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617144341_AddIsMicrosoftSource') THEN
    ALTER TABLE "IdeaSources" ADD "IsMicrosoftSource" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617144341_AddIsMicrosoftSource') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260617144341_AddIsMicrosoftSource', '10.0.7');
    END IF;
END $EF$;
COMMIT;

