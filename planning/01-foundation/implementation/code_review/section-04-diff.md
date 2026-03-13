diff --git a/planning/01-foundation/implementation/deep_implement_config.json b/planning/01-foundation/implementation/deep_implement_config.json
index 900bb58..1c455c6 100644
--- a/planning/01-foundation/implementation/deep_implement_config.json
+++ b/planning/01-foundation/implementation/deep_implement_config.json
@@ -25,6 +25,10 @@
     "section-02-domain": {
       "status": "complete",
       "commit_hash": "925a434d590e303a61f0a7e122cae25e7c951dcd"
+    },
+    "section-03-application": {
+      "status": "complete",
+      "commit_hash": "57d7f05"
     }
   },
   "pre_commit": {
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs b/src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs
new file mode 100644
index 0000000..3e7f180
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs
@@ -0,0 +1,24 @@
+using System.Reflection;
+using Microsoft.EntityFrameworkCore;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Infrastructure.Data;
+
+public class ApplicationDbContext : DbContext, IApplicationDbContext
+{
+    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
+
+    public DbSet<Content> Contents => Set<Content>();
+    public DbSet<Platform> Platforms => Set<Platform>();
+    public DbSet<BrandProfile> BrandProfiles => Set<BrandProfile>();
+    public DbSet<ContentCalendarSlot> ContentCalendarSlots => Set<ContentCalendarSlot>();
+    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
+    public DbSet<User> Users => Set<User>();
+
+    protected override void OnModelCreating(ModelBuilder modelBuilder)
+    {
+        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
+        base.OnModelCreating(modelBuilder);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/AuditLogEntryConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/AuditLogEntryConfiguration.cs
new file mode 100644
index 0000000..99e3264
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/AuditLogEntryConfiguration.cs
@@ -0,0 +1,25 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
+{
+    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
+    {
+        builder.ToTable("AuditLogEntries");
+
+        builder.HasKey(a => a.Id);
+
+        builder.HasIndex(a => a.Timestamp);
+
+        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(200);
+        builder.Property(a => a.Action).IsRequired().HasMaxLength(50);
+        builder.Property(a => a.OldValue).HasColumnType("text");
+        builder.Property(a => a.NewValue).HasColumnType("text");
+        builder.Property(a => a.Details).HasMaxLength(2000);
+
+        builder.Ignore(a => a.DomainEvents);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/BrandProfileConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/BrandProfileConfiguration.cs
new file mode 100644
index 0000000..68580e4
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/BrandProfileConfiguration.cs
@@ -0,0 +1,30 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class BrandProfileConfiguration : IEntityTypeConfiguration<BrandProfile>
+{
+    public void Configure(EntityTypeBuilder<BrandProfile> builder)
+    {
+        builder.ToTable("BrandProfiles");
+
+        builder.HasKey(b => b.Id);
+
+        builder.Property(b => b.Name).IsRequired().HasMaxLength(200);
+        builder.Property(b => b.PersonaDescription).HasMaxLength(2000);
+        builder.Property(b => b.StyleGuidelines).HasMaxLength(4000);
+
+        builder.Property(b => b.VocabularyPreferences)
+            .HasConversion(new JsonValueConverter<Domain.ValueObjects.VocabularyConfig>())
+            .HasColumnType("jsonb");
+
+        builder.Property<uint>("xmin")
+            .HasColumnType("xid")
+            .ValueGeneratedOnAddOrUpdate()
+            .IsConcurrencyToken();
+
+        builder.Ignore(b => b.DomainEvents);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentCalendarSlotConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentCalendarSlotConfiguration.cs
new file mode 100644
index 0000000..c58aaa0
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentCalendarSlotConfiguration.cs
@@ -0,0 +1,28 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class ContentCalendarSlotConfiguration : IEntityTypeConfiguration<ContentCalendarSlot>
+{
+    public void Configure(EntityTypeBuilder<ContentCalendarSlot> builder)
+    {
+        builder.ToTable("ContentCalendarSlots");
+
+        builder.HasKey(s => s.Id);
+
+        builder.HasIndex(s => new { s.ScheduledDate, s.TargetPlatform });
+
+        builder.Property(s => s.TimeZoneId).IsRequired().HasMaxLength(100);
+        builder.Property(s => s.Theme).HasMaxLength(200);
+        builder.Property(s => s.RecurrencePattern).HasMaxLength(200);
+
+        builder.HasOne<Content>()
+            .WithMany()
+            .HasForeignKey(s => s.ContentId)
+            .OnDelete(DeleteBehavior.SetNull);
+
+        builder.Ignore(s => s.DomainEvents);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentConfiguration.cs
new file mode 100644
index 0000000..d368728
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentConfiguration.cs
@@ -0,0 +1,46 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class ContentConfiguration : IEntityTypeConfiguration<Content>
+{
+    public void Configure(EntityTypeBuilder<Content> builder)
+    {
+        builder.ToTable("Contents");
+
+        builder.HasKey(c => c.Id);
+
+        builder.Property(c => c.Body).IsRequired();
+        builder.Property(c => c.Title).HasMaxLength(500);
+        builder.Property(c => c.ContentType).IsRequired();
+        builder.Property(c => c.Status).IsRequired();
+
+        builder.Property(c => c.Metadata)
+            .HasConversion(new JsonValueConverter<Domain.ValueObjects.ContentMetadata>())
+            .HasColumnType("jsonb");
+
+        builder.Property(c => c.TargetPlatforms)
+            .HasColumnType("integer[]");
+
+        builder.HasIndex(c => c.Status);
+        builder.HasIndex(c => c.ScheduledAt);
+
+        builder.Property<uint>("xmin")
+            .HasColumnType("xid")
+            .ValueGeneratedOnAddOrUpdate()
+            .IsConcurrencyToken();
+
+        builder.HasQueryFilter(c => c.Status != ContentStatus.Archived);
+
+        builder.Property(c => c.ParentContentId);
+        builder.HasOne<Content>()
+            .WithMany()
+            .HasForeignKey(c => c.ParentContentId)
+            .OnDelete(DeleteBehavior.SetNull);
+
+        builder.Ignore(c => c.DomainEvents);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/JsonValueConverter.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/JsonValueConverter.cs
new file mode 100644
index 0000000..b6ac2cd
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/JsonValueConverter.cs
@@ -0,0 +1,18 @@
+using System.Text.Json;
+using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class JsonValueConverter<T> : ValueConverter<T, string> where T : class, new()
+{
+    private static readonly JsonSerializerOptions Options = new()
+    {
+        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
+    };
+
+    public JsonValueConverter() : base(
+        v => JsonSerializer.Serialize(v, Options),
+        v => JsonSerializer.Deserialize<T>(v, Options) ?? new T())
+    {
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/PlatformConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/PlatformConfiguration.cs
new file mode 100644
index 0000000..1428bb1
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/PlatformConfiguration.cs
@@ -0,0 +1,35 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class PlatformConfiguration : IEntityTypeConfiguration<Platform>
+{
+    public void Configure(EntityTypeBuilder<Platform> builder)
+    {
+        builder.ToTable("Platforms");
+
+        builder.HasKey(p => p.Id);
+
+        builder.HasIndex(p => p.Type).IsUnique();
+
+        builder.Property(p => p.DisplayName).IsRequired().HasMaxLength(100);
+        builder.Property(p => p.EncryptedAccessToken);
+        builder.Property(p => p.EncryptedRefreshToken);
+
+        builder.Property(p => p.RateLimitState)
+            .HasConversion(new JsonValueConverter<Domain.ValueObjects.PlatformRateLimitState>())
+            .HasColumnType("jsonb");
+        builder.Property(p => p.Settings)
+            .HasConversion(new JsonValueConverter<Domain.ValueObjects.PlatformSettings>())
+            .HasColumnType("jsonb");
+
+        builder.Property<uint>("xmin")
+            .HasColumnType("xid")
+            .ValueGeneratedOnAddOrUpdate()
+            .IsConcurrencyToken();
+
+        builder.Ignore(p => p.DomainEvents);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/UserConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/UserConfiguration.cs
new file mode 100644
index 0000000..57f7e8d
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/UserConfiguration.cs
@@ -0,0 +1,27 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class UserConfiguration : IEntityTypeConfiguration<User>
+{
+    public void Configure(EntityTypeBuilder<User> builder)
+    {
+        builder.ToTable("Users");
+
+        builder.HasKey(u => u.Id);
+
+        builder.HasIndex(u => u.Email).IsUnique();
+
+        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
+        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(200);
+        builder.Property(u => u.TimeZoneId).IsRequired().HasMaxLength(100);
+
+        builder.Property(u => u.Settings)
+            .HasConversion(new JsonValueConverter<Domain.ValueObjects.UserSettings>())
+            .HasColumnType("jsonb");
+
+        builder.Ignore(u => u.DomainEvents);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Interceptors/AuditLogInterceptor.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Interceptors/AuditLogInterceptor.cs
new file mode 100644
index 0000000..e7fa5c9
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Interceptors/AuditLogInterceptor.cs
@@ -0,0 +1,96 @@
+using System.Text.Json;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.ChangeTracking;
+using Microsoft.EntityFrameworkCore.Diagnostics;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Interceptors;
+
+public class AuditLogInterceptor : SaveChangesInterceptor
+{
+    private const int MaxValueLength = 4096;
+    private static readonly HashSet<string> ExcludedPatterns =
+        ["Encrypted", "Token", "Password", "Secret"];
+
+    private readonly IDateTimeProvider _dateTimeProvider;
+
+    public AuditLogInterceptor(IDateTimeProvider dateTimeProvider)
+    {
+        _dateTimeProvider = dateTimeProvider;
+    }
+
+    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
+        DbContextEventData eventData,
+        InterceptionResult<int> result,
+        CancellationToken cancellationToken = default)
+    {
+        if (eventData.Context is not null)
+        {
+            CreateAuditEntries(eventData.Context);
+        }
+
+        return base.SavingChangesAsync(eventData, result, cancellationToken);
+    }
+
+    private void CreateAuditEntries(DbContext context)
+    {
+        var entries = context.ChangeTracker.Entries<EntityBase>()
+            .Where(e => e.Entity is not AuditLogEntry)
+            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
+            .ToList();
+
+        var now = _dateTimeProvider.UtcNow;
+
+        foreach (var entry in entries)
+        {
+            var auditEntry = new AuditLogEntry
+            {
+                EntityType = entry.Metadata.ClrType.Name,
+                EntityId = entry.Entity.Id,
+                Action = entry.State switch
+                {
+                    EntityState.Added => "Created",
+                    EntityState.Modified => "Modified",
+                    EntityState.Deleted => "Deleted",
+                    _ => "Unknown"
+                },
+                Timestamp = now,
+                OldValue = entry.State is EntityState.Modified or EntityState.Deleted
+                    ? Truncate(SerializeValues(entry, useOriginal: true))
+                    : null,
+                NewValue = entry.State is EntityState.Added or EntityState.Modified
+                    ? Truncate(SerializeValues(entry, useOriginal: false))
+                    : null,
+            };
+
+            context.Set<AuditLogEntry>().Add(auditEntry);
+        }
+    }
+
+    private static string SerializeValues(EntityEntry entry, bool useOriginal)
+    {
+        var values = new Dictionary<string, object?>();
+
+        foreach (var property in entry.Properties)
+        {
+            var propertyName = property.Metadata.Name;
+
+            if (ExcludedPatterns.Any(p => propertyName.Contains(p, StringComparison.OrdinalIgnoreCase)))
+                continue;
+
+            values[propertyName] = useOriginal
+                ? property.OriginalValue
+                : property.CurrentValue;
+        }
+
+        return JsonSerializer.Serialize(values);
+    }
+
+    private static string? Truncate(string? value)
+    {
+        if (value is null) return null;
+        return value.Length > MaxValueLength ? value[..MaxValueLength] : value;
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Interceptors/AuditableInterceptor.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Interceptors/AuditableInterceptor.cs
new file mode 100644
index 0000000..7522ad4
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Interceptors/AuditableInterceptor.cs
@@ -0,0 +1,47 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Diagnostics;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Common;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Interceptors;
+
+public class AuditableInterceptor : SaveChangesInterceptor
+{
+    private readonly IDateTimeProvider _dateTimeProvider;
+
+    public AuditableInterceptor(IDateTimeProvider dateTimeProvider)
+    {
+        _dateTimeProvider = dateTimeProvider;
+    }
+
+    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
+        DbContextEventData eventData,
+        InterceptionResult<int> result,
+        CancellationToken cancellationToken = default)
+    {
+        if (eventData.Context is not null)
+        {
+            UpdateAuditableEntities(eventData.Context);
+        }
+
+        return base.SavingChangesAsync(eventData, result, cancellationToken);
+    }
+
+    private void UpdateAuditableEntities(DbContext context)
+    {
+        var now = _dateTimeProvider.UtcNow;
+
+        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
+        {
+            if (entry.State == EntityState.Added)
+            {
+                entry.Entity.CreatedAt = now;
+                entry.Entity.UpdatedAt = now;
+            }
+            else if (entry.State == EntityState.Modified)
+            {
+                entry.Entity.UpdatedAt = now;
+            }
+        }
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
new file mode 100644
index 0000000..bfabba4
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
@@ -0,0 +1,51 @@
+using Microsoft.AspNetCore.DataProtection;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Extensions.DependencyInjection;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Infrastructure.Data;
+using PersonalBrandAssistant.Infrastructure.Data.Interceptors;
+using PersonalBrandAssistant.Infrastructure.Services;
+
+namespace PersonalBrandAssistant.Infrastructure;
+
+public static class DependencyInjection
+{
+    public static IServiceCollection AddInfrastructure(
+        this IServiceCollection services,
+        IConfiguration configuration)
+    {
+        var connectionString = configuration.GetConnectionString("DefaultConnection")
+            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
+
+        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
+        services.AddScoped<AuditableInterceptor>();
+        services.AddScoped<AuditLogInterceptor>();
+
+        services.AddDbContext<ApplicationDbContext>((sp, options) =>
+        {
+            options.UseNpgsql(connectionString);
+            options.AddInterceptors(
+                sp.GetRequiredService<AuditableInterceptor>(),
+                sp.GetRequiredService<AuditLogInterceptor>());
+        });
+
+        services.AddScoped<IApplicationDbContext>(sp =>
+            sp.GetRequiredService<ApplicationDbContext>());
+
+        services.AddDataProtection()
+            .PersistKeysToFileSystem(new DirectoryInfo(
+                configuration["DataProtection:KeyPath"] ?? "data-protection-keys"))
+            .SetApplicationName("PersonalBrandAssistant");
+
+        services.AddSingleton<IEncryptionService, EncryptionService>();
+
+        services.AddHostedService<DataSeeder>();
+        services.AddHostedService<AuditLogCleanupService>();
+
+        services.AddHealthChecks()
+            .AddDbContextCheck<ApplicationDbContext>();
+
+        return services;
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/AuditLogCleanupService.cs b/src/PersonalBrandAssistant.Infrastructure/Services/AuditLogCleanupService.cs
new file mode 100644
index 0000000..ca1738b
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/AuditLogCleanupService.cs
@@ -0,0 +1,58 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Infrastructure.Data;
+
+namespace PersonalBrandAssistant.Infrastructure.Services;
+
+public class AuditLogCleanupService : BackgroundService
+{
+    private readonly IServiceScopeFactory _scopeFactory;
+    private readonly ILogger<AuditLogCleanupService> _logger;
+    private readonly int _retentionDays;
+
+    public AuditLogCleanupService(
+        IServiceScopeFactory scopeFactory,
+        IConfiguration configuration,
+        ILogger<AuditLogCleanupService> logger)
+    {
+        _scopeFactory = scopeFactory;
+        _logger = logger;
+        _retentionDays = configuration.GetValue("AuditLog:RetentionDays", 90);
+    }
+
+    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
+    {
+        while (!stoppingToken.IsCancellationRequested)
+        {
+            try
+            {
+                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
+
+                using var scope = _scopeFactory.CreateScope();
+                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
+
+                var cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionDays);
+                var deleted = await context.AuditLogEntries
+                    .Where(e => e.Timestamp < cutoff)
+                    .ExecuteDeleteAsync(stoppingToken);
+
+                if (deleted > 0)
+                {
+                    _logger.LogInformation("Deleted {Count} audit log entries older than {Days} days",
+                        deleted, _retentionDays);
+                }
+            }
+            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
+            {
+                break;
+            }
+            catch (Exception ex)
+            {
+                _logger.LogError(ex, "Error during audit log cleanup");
+            }
+        }
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/DataSeeder.cs b/src/PersonalBrandAssistant.Infrastructure/Services/DataSeeder.cs
new file mode 100644
index 0000000..2b84140
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/DataSeeder.cs
@@ -0,0 +1,71 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Data;
+
+namespace PersonalBrandAssistant.Infrastructure.Services;
+
+public class DataSeeder : IHostedService
+{
+    private readonly IServiceScopeFactory _scopeFactory;
+    private readonly IConfiguration _configuration;
+    private readonly ILogger<DataSeeder> _logger;
+
+    public DataSeeder(
+        IServiceScopeFactory scopeFactory,
+        IConfiguration configuration,
+        ILogger<DataSeeder> logger)
+    {
+        _scopeFactory = scopeFactory;
+        _configuration = configuration;
+        _logger = logger;
+    }
+
+    public async Task StartAsync(CancellationToken cancellationToken)
+    {
+        using var scope = _scopeFactory.CreateScope();
+        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
+
+        if (!await context.BrandProfiles.AnyAsync(cancellationToken))
+        {
+            context.BrandProfiles.Add(new BrandProfile
+            {
+                Name = "Default Profile",
+                PersonaDescription = "Default brand persona",
+                IsActive = true,
+            });
+            _logger.LogInformation("Seeded default BrandProfile");
+        }
+
+        if (!await context.Platforms.AnyAsync(cancellationToken))
+        {
+            var platforms = Enum.GetValues<PlatformType>().Select(type => new Platform
+            {
+                Type = type,
+                DisplayName = type.ToString(),
+                IsConnected = false,
+            });
+            context.Platforms.AddRange(platforms);
+            _logger.LogInformation("Seeded {Count} Platform records", Enum.GetValues<PlatformType>().Length);
+        }
+
+        if (!await context.Users.AnyAsync(cancellationToken))
+        {
+            context.Users.Add(new User
+            {
+                Email = _configuration["DefaultUser:Email"] ?? "user@example.com",
+                DisplayName = "Default User",
+                TimeZoneId = _configuration["DefaultUser:TimeZoneId"] ?? "America/New_York",
+            });
+            _logger.LogInformation("Seeded default User");
+        }
+
+        await context.SaveChangesAsync(cancellationToken);
+    }
+
+    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/DateTimeProvider.cs b/src/PersonalBrandAssistant.Infrastructure/Services/DateTimeProvider.cs
new file mode 100644
index 0000000..89a6044
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/DateTimeProvider.cs
@@ -0,0 +1,8 @@
+using PersonalBrandAssistant.Application.Common.Interfaces;
+
+namespace PersonalBrandAssistant.Infrastructure.Services;
+
+public class DateTimeProvider : IDateTimeProvider
+{
+    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/EncryptionService.cs b/src/PersonalBrandAssistant.Infrastructure/Services/EncryptionService.cs
new file mode 100644
index 0000000..7f922a0
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/EncryptionService.cs
@@ -0,0 +1,25 @@
+using System.Text;
+using Microsoft.AspNetCore.DataProtection;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+
+namespace PersonalBrandAssistant.Infrastructure.Services;
+
+public class EncryptionService : IEncryptionService
+{
+    private readonly IDataProtector _protector;
+
+    public EncryptionService(IDataProtectionProvider provider)
+    {
+        _protector = provider.CreateProtector("PersonalBrandAssistant.Secrets");
+    }
+
+    public byte[] Encrypt(string plaintext)
+    {
+        return _protector.Protect(Encoding.UTF8.GetBytes(plaintext));
+    }
+
+    public string Decrypt(byte[] ciphertext)
+    {
+        return Encoding.UTF8.GetString(_protector.Unprotect(ciphertext));
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Interceptors/AuditLogInterceptorTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Interceptors/AuditLogInterceptorTests.cs
new file mode 100644
index 0000000..9b5b792
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Interceptors/AuditLogInterceptorTests.cs
@@ -0,0 +1,113 @@
+using Microsoft.EntityFrameworkCore;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Interceptors;
+
+[Collection("Postgres")]
+public class AuditLogInterceptorTests : IAsyncLifetime
+{
+    private readonly PostgresFixture _fixture;
+    private readonly string _connectionString;
+
+    public AuditLogInterceptorTests(PostgresFixture fixture)
+    {
+        _fixture = fixture;
+        _connectionString = fixture.GetUniqueConnectionString();
+    }
+
+    public async Task InitializeAsync()
+    {
+        await using var ctx = _fixture.CreateDbContext(connectionString: _connectionString);
+        await ctx.Database.EnsureCreatedAsync();
+    }
+
+    public async Task DisposeAsync()
+    {
+        await using var ctx = _fixture.CreateDbContext(connectionString: _connectionString);
+        await ctx.Database.EnsureDeletedAsync();
+    }
+
+    [Fact]
+    public async Task ModifyContent_CreatesAuditLogEntry()
+    {
+        var dateTimeProvider = new Mock<IDateTimeProvider>();
+        dateTimeProvider.Setup(d => d.UtcNow).Returns(DateTimeOffset.UtcNow);
+
+        await using var context = _fixture.CreateDbContext(dateTimeProvider.Object, _connectionString);
+
+        var content = Content.Create(ContentType.BlogPost, "Original body");
+        context.Contents.Add(content);
+        await context.SaveChangesAsync();
+
+        content.Body = "Modified body";
+        await context.SaveChangesAsync();
+
+        var auditEntries = await context.AuditLogEntries
+            .Where(a => a.EntityId == content.Id && a.Action == "Modified")
+            .ToListAsync();
+
+        Assert.NotEmpty(auditEntries);
+        Assert.Equal("Content", auditEntries.First().EntityType);
+    }
+
+    [Fact]
+    public async Task AuditEntry_ExcludesEncryptedFields()
+    {
+        var dateTimeProvider = new Mock<IDateTimeProvider>();
+        dateTimeProvider.Setup(d => d.UtcNow).Returns(DateTimeOffset.UtcNow);
+
+        await using var context = _fixture.CreateDbContext(dateTimeProvider.Object, _connectionString);
+
+        var platform = new Platform
+        {
+            Type = PlatformType.TwitterX,
+            DisplayName = "Twitter/X",
+            IsConnected = true,
+            EncryptedAccessToken = new byte[] { 1, 2, 3 },
+        };
+        context.Platforms.Add(platform);
+        await context.SaveChangesAsync();
+
+        var auditEntry = await context.AuditLogEntries
+            .FirstOrDefaultAsync(a => a.EntityId == platform.Id && a.Action == "Created");
+
+        Assert.NotNull(auditEntry);
+        Assert.DoesNotContain("EncryptedAccessToken", auditEntry!.NewValue ?? "");
+        Assert.DoesNotContain("EncryptedRefreshToken", auditEntry.NewValue ?? "");
+    }
+
+    [Fact]
+    public async Task AuditEntry_OldValueNewValue_AreValidJson()
+    {
+        var dateTimeProvider = new Mock<IDateTimeProvider>();
+        dateTimeProvider.Setup(d => d.UtcNow).Returns(DateTimeOffset.UtcNow);
+
+        await using var context = _fixture.CreateDbContext(dateTimeProvider.Object, _connectionString);
+
+        var content = Content.Create(ContentType.BlogPost, "Original");
+        context.Contents.Add(content);
+        await context.SaveChangesAsync();
+
+        content.Body = "Updated";
+        await context.SaveChangesAsync();
+
+        var auditEntry = await context.AuditLogEntries
+            .FirstOrDefaultAsync(a => a.EntityId == content.Id && a.Action == "Modified");
+
+        Assert.NotNull(auditEntry);
+        Assert.NotNull(auditEntry!.NewValue);
+
+        var newDoc = System.Text.Json.JsonDocument.Parse(auditEntry.NewValue!);
+        Assert.NotNull(newDoc);
+
+        if (auditEntry.OldValue is not null)
+        {
+            var oldDoc = System.Text.Json.JsonDocument.Parse(auditEntry.OldValue);
+            Assert.NotNull(oldDoc);
+        }
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Interceptors/AuditableInterceptorTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Interceptors/AuditableInterceptorTests.cs
new file mode 100644
index 0000000..36c9fe8
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Interceptors/AuditableInterceptorTests.cs
@@ -0,0 +1,97 @@
+using Microsoft.EntityFrameworkCore;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Interceptors;
+
+[Collection("Postgres")]
+public class AuditableInterceptorTests : IAsyncLifetime
+{
+    private readonly PostgresFixture _fixture;
+    private readonly string _connectionString;
+
+    public AuditableInterceptorTests(PostgresFixture fixture)
+    {
+        _fixture = fixture;
+        _connectionString = fixture.GetUniqueConnectionString();
+    }
+
+    public async Task InitializeAsync()
+    {
+        await using var ctx = _fixture.CreateDbContext(connectionString: _connectionString);
+        await ctx.Database.EnsureCreatedAsync();
+    }
+
+    public async Task DisposeAsync()
+    {
+        await using var ctx = _fixture.CreateDbContext(connectionString: _connectionString);
+        await ctx.Database.EnsureDeletedAsync();
+    }
+
+    [Fact]
+    public async Task Insert_SetsCreatedAtAndUpdatedAt()
+    {
+        var mockTime = new DateTimeOffset(2026, 3, 13, 12, 0, 0, TimeSpan.Zero);
+        var dateTimeProvider = new Mock<IDateTimeProvider>();
+        dateTimeProvider.Setup(d => d.UtcNow).Returns(mockTime);
+
+        await using var context = _fixture.CreateDbContext(dateTimeProvider.Object, _connectionString);
+
+        var content = Content.Create(ContentType.BlogPost, "Test body");
+        context.Contents.Add(content);
+        await context.SaveChangesAsync();
+
+        Assert.Equal(mockTime, content.CreatedAt);
+        Assert.Equal(mockTime, content.UpdatedAt);
+    }
+
+    [Fact]
+    public async Task Update_UpdatesUpdatedAtPreservesCreatedAt()
+    {
+        var initialTime = new DateTimeOffset(2026, 3, 13, 10, 0, 0, TimeSpan.Zero);
+        var updateTime = new DateTimeOffset(2026, 3, 13, 14, 0, 0, TimeSpan.Zero);
+
+        var dateTimeProvider = new Mock<IDateTimeProvider>();
+        dateTimeProvider.Setup(d => d.UtcNow).Returns(initialTime);
+
+        await using var context = _fixture.CreateDbContext(dateTimeProvider.Object, _connectionString);
+
+        var content = Content.Create(ContentType.BlogPost, "Test body");
+        context.Contents.Add(content);
+        await context.SaveChangesAsync();
+
+        dateTimeProvider.Setup(d => d.UtcNow).Returns(updateTime);
+        content.Body = "Updated body";
+        await context.SaveChangesAsync();
+
+        Assert.Equal(initialTime, content.CreatedAt);
+        Assert.Equal(updateTime, content.UpdatedAt);
+    }
+
+    [Fact]
+    public async Task AuditLogEntry_NotAffectedByAuditableInterceptor()
+    {
+        var mockTime = new DateTimeOffset(2026, 3, 13, 12, 0, 0, TimeSpan.Zero);
+        var dateTimeProvider = new Mock<IDateTimeProvider>();
+        dateTimeProvider.Setup(d => d.UtcNow).Returns(mockTime);
+
+        await using var context = _fixture.CreateDbContext(dateTimeProvider.Object, _connectionString);
+
+        var entry = new AuditLogEntry
+        {
+            EntityType = "Test",
+            EntityId = Guid.NewGuid(),
+            Action = "TestAction",
+            Timestamp = DateTimeOffset.UtcNow,
+        };
+
+        context.AuditLogEntries.Add(entry);
+        await context.SaveChangesAsync();
+
+        var saved = await context.AuditLogEntries.FirstAsync(a => a.Id == entry.Id);
+        Assert.NotNull(saved);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs
new file mode 100644
index 0000000..9417722
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs
@@ -0,0 +1,98 @@
+using Microsoft.EntityFrameworkCore;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Data;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Persistence;
+
+public class ApplicationDbContextConfigurationTests
+{
+    private static ApplicationDbContext CreateInMemoryContext()
+    {
+        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseNpgsql("Host=localhost;Database=fake")
+            .Options;
+
+        return new ApplicationDbContext(options);
+    }
+
+    [Fact]
+    public void Content_HasQueryFilter()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(Content))!;
+        var queryFilters = entityType.GetDeclaredQueryFilters();
+
+        Assert.NotEmpty(queryFilters);
+    }
+
+    [Fact]
+    public void Content_HasConcurrencyToken()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(Content))!;
+        var xmin = entityType.FindProperty("xmin");
+
+        Assert.NotNull(xmin);
+        Assert.True(xmin!.IsConcurrencyToken);
+    }
+
+    [Fact]
+    public void Platform_Type_HasUniqueIndex()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(Platform))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(Platform.Type)));
+
+        Assert.NotNull(index);
+        Assert.True(index!.IsUnique);
+    }
+
+    [Fact]
+    public void ContentCalendarSlot_HasCompositeIndex()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(ContentCalendarSlot))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Count == 2 &&
+                i.Properties.Any(p => p.Name == nameof(ContentCalendarSlot.ScheduledDate)) &&
+                i.Properties.Any(p => p.Name == nameof(ContentCalendarSlot.TargetPlatform)));
+
+        Assert.NotNull(index);
+    }
+
+    [Fact]
+    public void AuditLogEntry_Timestamp_HasIndex()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(AuditLogEntry))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(AuditLogEntry.Timestamp)));
+
+        Assert.NotNull(index);
+    }
+
+    [Fact]
+    public void User_Email_HasUniqueIndex()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(User))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(User.Email)));
+
+        Assert.NotNull(index);
+        Assert.True(index!.IsUnique);
+    }
+
+    [Fact]
+    public void Content_Status_HasIndex()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(Content))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "Status"));
+
+        Assert.NotNull(index);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/DataSeederTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/DataSeederTests.cs
new file mode 100644
index 0000000..08c68be
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/DataSeederTests.cs
@@ -0,0 +1,107 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging.Abstractions;
+using PersonalBrandAssistant.Infrastructure.Data;
+using PersonalBrandAssistant.Infrastructure.Data.Interceptors;
+using PersonalBrandAssistant.Infrastructure.Services;
+using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services;
+
+[Collection("Postgres")]
+public class DataSeederTests : IAsyncLifetime
+{
+    private readonly PostgresFixture _fixture;
+    private readonly string _connectionString;
+    private ServiceProvider _serviceProvider = null!;
+    private IServiceScopeFactory _scopeFactory = null!;
+    private DataSeeder _seeder = null!;
+
+    public DataSeederTests(PostgresFixture fixture)
+    {
+        _fixture = fixture;
+        _connectionString = fixture.GetUniqueConnectionString();
+    }
+
+    public async Task InitializeAsync()
+    {
+        var services = new ServiceCollection();
+        var dateTimeProvider = new DateTimeProvider();
+
+        services.AddDbContext<ApplicationDbContext>(options =>
+        {
+            options.UseNpgsql(_connectionString);
+            options.AddInterceptors(
+                new AuditableInterceptor(dateTimeProvider),
+                new AuditLogInterceptor(dateTimeProvider));
+        });
+
+        var configuration = new ConfigurationBuilder()
+            .AddInMemoryCollection(new Dictionary<string, string?>
+            {
+                ["DefaultUser:Email"] = "test@test.com",
+                ["DefaultUser:TimeZoneId"] = "UTC",
+            })
+            .Build();
+
+        _serviceProvider = services.BuildServiceProvider();
+        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
+
+        using var scope = _scopeFactory.CreateScope();
+        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
+        await ctx.Database.EnsureCreatedAsync();
+
+        _seeder = new DataSeeder(_scopeFactory, configuration, NullLogger<DataSeeder>.Instance);
+    }
+
+    public async Task DisposeAsync()
+    {
+        using var scope = _scopeFactory.CreateScope();
+        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
+        await ctx.Database.EnsureDeletedAsync();
+        await _serviceProvider.DisposeAsync();
+    }
+
+    [Fact]
+    public async Task StartAsync_SeedsDefaultBrandProfile()
+    {
+        await _seeder.StartAsync(CancellationToken.None);
+
+        using var scope = _scopeFactory.CreateScope();
+        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
+        Assert.True(await context.BrandProfiles.AnyAsync());
+    }
+
+    [Fact]
+    public async Task StartAsync_Seeds4Platforms()
+    {
+        await _seeder.StartAsync(CancellationToken.None);
+
+        using var scope = _scopeFactory.CreateScope();
+        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
+        Assert.Equal(4, await context.Platforms.CountAsync());
+    }
+
+    [Fact]
+    public async Task StartAsync_SeedsDefaultUser()
+    {
+        await _seeder.StartAsync(CancellationToken.None);
+
+        using var scope = _scopeFactory.CreateScope();
+        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
+        Assert.True(await context.Users.AnyAsync());
+    }
+
+    [Fact]
+    public async Task StartAsync_Idempotent_DoesNotDuplicateRecords()
+    {
+        await _seeder.StartAsync(CancellationToken.None);
+        await _seeder.StartAsync(CancellationToken.None);
+
+        using var scope = _scopeFactory.CreateScope();
+        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
+        Assert.Equal(4, await context.Platforms.CountAsync());
+        Assert.Equal(1, await context.BrandProfiles.CountAsync());
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/EncryptionServiceTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/EncryptionServiceTests.cs
new file mode 100644
index 0000000..9488d9b
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/EncryptionServiceTests.cs
@@ -0,0 +1,57 @@
+using Microsoft.AspNetCore.DataProtection;
+using PersonalBrandAssistant.Infrastructure.Services;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services;
+
+public class EncryptionServiceTests
+{
+    private readonly EncryptionService _service;
+
+    public EncryptionServiceTests()
+    {
+        var provider = DataProtectionProvider.Create("TestApp");
+        _service = new EncryptionService(provider);
+    }
+
+    [Fact]
+    public void Encrypt_ReturnsNonEmptyBytes()
+    {
+        var encrypted = _service.Encrypt("test-secret");
+
+        Assert.NotNull(encrypted);
+        Assert.NotEmpty(encrypted);
+    }
+
+    [Fact]
+    public void Decrypt_ReturnsOriginalPlaintext()
+    {
+        var plaintext = "my-api-key-12345";
+        var encrypted = _service.Encrypt(plaintext);
+        var decrypted = _service.Decrypt(encrypted);
+
+        Assert.Equal(plaintext, decrypted);
+    }
+
+    [Fact]
+    public void Encrypt_SamePlaintextTwice_ProducesDifferentCiphertext()
+    {
+        var plaintext = "same-value";
+        var encrypted1 = _service.Encrypt(plaintext);
+        var encrypted2 = _service.Encrypt(plaintext);
+
+        Assert.NotEqual(encrypted1, encrypted2);
+    }
+
+    [Theory]
+    [InlineData("")]
+    [InlineData("simple")]
+    [InlineData("unicode-🎉-emoji")]
+    [InlineData("a-very-long-string-that-goes-on-and-on-for-quite-a-while-to-test-handling-of-larger-inputs")]
+    public void Roundtrip_VariousStrings_ReturnOriginal(string plaintext)
+    {
+        var encrypted = _service.Encrypt(plaintext);
+        var decrypted = _service.Decrypt(encrypted);
+
+        Assert.Equal(plaintext, decrypted);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/TestFixtures/PostgresFixture.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/TestFixtures/PostgresFixture.cs
new file mode 100644
index 0000000..979afc0
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/TestFixtures/PostgresFixture.cs
@@ -0,0 +1,55 @@
+using Microsoft.EntityFrameworkCore;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Infrastructure.Data;
+using PersonalBrandAssistant.Infrastructure.Data.Interceptors;
+using PersonalBrandAssistant.Infrastructure.Services;
+using Testcontainers.PostgreSql;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
+
+public class PostgresFixture : IAsyncLifetime
+{
+    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
+        .Build();
+
+    public string ConnectionString => _container.GetConnectionString();
+
+    public async Task InitializeAsync()
+    {
+        await _container.StartAsync();
+    }
+
+    public async Task DisposeAsync()
+    {
+        await _container.DisposeAsync();
+    }
+
+    public string GetUniqueConnectionString()
+    {
+        var dbName = $"test_{Guid.NewGuid():N}"[..20];
+        var builder = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString)
+        {
+            Database = dbName
+        };
+        return builder.ConnectionString;
+    }
+
+    public ApplicationDbContext CreateDbContext(
+        IDateTimeProvider? dateTimeProvider = null,
+        string? connectionString = null)
+    {
+        var provider = dateTimeProvider ?? new DateTimeProvider();
+        var connStr = connectionString ?? ConnectionString;
+        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseNpgsql(connStr)
+            .AddInterceptors(
+                new AuditableInterceptor(provider),
+                new AuditLogInterceptor(provider))
+            .Options;
+
+        return new ApplicationDbContext(options);
+    }
+}
+
+[CollectionDefinition("Postgres")]
+public class PostgresCollection : ICollectionFixture<PostgresFixture>;
