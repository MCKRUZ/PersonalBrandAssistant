START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617141029_AddDigestKind') THEN
    DROP INDEX "IX_Digests_Date";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617141029_AddDigestKind') THEN
    ALTER TABLE "Digests" ADD "Kind" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617141029_AddDigestKind') THEN
    CREATE UNIQUE INDEX "IX_Digests_Date_Kind" ON "Digests" ("Date", "Kind");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617141029_AddDigestKind') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260617141029_AddDigestKind', '10.0.7');
    END IF;
END $EF$;
COMMIT;

