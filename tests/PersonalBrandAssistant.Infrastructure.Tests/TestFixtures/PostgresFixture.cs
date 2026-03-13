using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Infrastructure.Data;
using PersonalBrandAssistant.Infrastructure.Data.Interceptors;
using PersonalBrandAssistant.Infrastructure.Services;
using Testcontainers.PostgreSql;

namespace PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public string GetUniqueConnectionString()
    {
        var dbName = $"test_{Guid.NewGuid():N}"[..20];
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = dbName
        };
        return builder.ConnectionString;
    }

    public ApplicationDbContext CreateDbContext(
        IDateTimeProvider? dateTimeProvider = null,
        string? connectionString = null)
    {
        var provider = dateTimeProvider ?? new DateTimeProvider();
        var connStr = connectionString ?? ConnectionString;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connStr)
            .AddInterceptors(
                new AuditableInterceptor(provider),
                new AuditLogInterceptor(provider))
            .Options;

        return new ApplicationDbContext(options);
    }
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
