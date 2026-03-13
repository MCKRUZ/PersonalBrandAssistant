using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PersonalBrandAssistant.Infrastructure.Data;
using PersonalBrandAssistant.Infrastructure.Data.Interceptors;
using PersonalBrandAssistant.Infrastructure.Services;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

[Collection("Postgres")]
public class DataSeederTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly string _connectionString;
    private ServiceProvider _serviceProvider = null!;
    private IServiceScopeFactory _scopeFactory = null!;
    private DataSeeder _seeder = null!;

    public DataSeederTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _connectionString = fixture.GetUniqueConnectionString();
    }

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        var dateTimeProvider = new DateTimeProvider();

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(_connectionString);
            options.AddInterceptors(
                new AuditableInterceptor(dateTimeProvider),
                new AuditLogInterceptor(dateTimeProvider));
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DefaultUser:Email"] = "test@test.com",
                ["DefaultUser:TimeZoneId"] = "UTC",
            })
            .Build();

        _serviceProvider = services.BuildServiceProvider();
        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        _seeder = new DataSeeder(_scopeFactory, configuration, NullLogger<DataSeeder>.Instance);
    }

    public async Task DisposeAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await ctx.Database.EnsureDeletedAsync();
        await _serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_SeedsDefaultBrandProfile()
    {
        await _seeder.StartAsync(CancellationToken.None);

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await context.BrandProfiles.AnyAsync());
    }

    [Fact]
    public async Task StartAsync_Seeds4Platforms()
    {
        await _seeder.StartAsync(CancellationToken.None);

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(4, await context.Platforms.CountAsync());
    }

    [Fact]
    public async Task StartAsync_SeedsDefaultUser()
    {
        await _seeder.StartAsync(CancellationToken.None);

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await context.Users.AnyAsync());
    }

    [Fact]
    public async Task StartAsync_Idempotent_DoesNotDuplicateRecords()
    {
        await _seeder.StartAsync(CancellationToken.None);
        await _seeder.StartAsync(CancellationToken.None);

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(4, await context.Platforms.CountAsync());
        Assert.Equal(1, await context.BrandProfiles.CountAsync());
    }
}
