using PBA.Migration;

var v1Connection = GetArg(args, "--v1-connection") ?? Environment.GetEnvironmentVariable("V1_CONNECTION_STRING");
var v2Connection = GetArg(args, "--v2-connection") ?? Environment.GetEnvironmentVariable("V2_CONNECTION_STRING");

if (string.IsNullOrEmpty(v1Connection) || string.IsNullOrEmpty(v2Connection))
{
    Console.WriteLine("Usage: PBA.Migration --v1-connection <conn> --v2-connection <conn>");
    Console.WriteLine("  Or set V1_CONNECTION_STRING and V2_CONNECTION_STRING environment variables.");
    return 1;
}

Console.WriteLine("PBA Data Migration: v1 -> v2");
Console.WriteLine(new string('=', 40));

var migrator = new DataMigrator(v1Connection, v2Connection);
var result = await migrator.MigrateAsync();

Console.WriteLine();
Console.WriteLine("Migration Complete");
Console.WriteLine(new string('=', 40));
Console.WriteLine($"  Sources migrated:     {result.SourcesMigrated}");
Console.WriteLine($"  Ideas migrated:       {result.IdeasMigrated}");
Console.WriteLine($"  Saved ideas migrated: {result.SavedIdeasMigrated}");
Console.WriteLine($"  Errors:               {result.Errors}");

return result.Errors > 0 ? 2 : 0;

static string? GetArg(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }
    return null;
}
