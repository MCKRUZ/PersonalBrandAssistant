using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PBA.Domain.Entities;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Infrastructure.Tests.Data;

public class SchemaUpdateTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public void IAppDbContext_Exposes_ContentPlatformPublishes_DbSet()
    {
        using var context = CreateContext();
        Application.Common.Interfaces.IAppDbContext appContext = context;
        Assert.NotNull(appContext.ContentPlatformPublishes);
    }

    [Fact]
    public void IAppDbContext_Exposes_BrandProfiles_DbSet()
    {
        using var context = CreateContext();
        Application.Common.Interfaces.IAppDbContext appContext = context;
        Assert.NotNull(appContext.BrandProfiles);
    }

    [Fact]
    public async Task Content_Has_HangfireJobId_Property()
    {
        using var context = CreateContext();
        var content = new Content { Title = "Test", HangfireJobId = "job-123" };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var loaded = await context.Contents.FindAsync(content.Id);
        Assert.Equal("job-123", loaded!.HangfireJobId);
    }

    [Fact]
    public async Task Content_Has_IsDeleted_Property_DefaultFalse()
    {
        using var context = CreateContext();
        var content = new Content { Title = "Test" };
        Assert.False(content.IsDeleted);

        context.Contents.Add(content);
        await context.SaveChangesAsync();

        var loaded = await context.Contents.FindAsync(content.Id);
        Assert.False(loaded!.IsDeleted);
    }

    [Fact]
    public async Task Content_Has_Children_NavigationProperty()
    {
        using var context = CreateContext();
        var parent = new Content { Title = "Parent" };
        var child1 = new Content { Title = "Child 1", ParentContentId = parent.Id };
        var child2 = new Content { Title = "Child 2", ParentContentId = parent.Id };

        context.Contents.AddRange(parent, child1, child2);
        await context.SaveChangesAsync();

        var loaded = await context.Contents
            .Include(c => c.Children)
            .FirstAsync(c => c.Id == parent.Id);

        Assert.Equal(2, loaded.Children.Count);
    }

    [Fact]
    public async Task SoftDelete_QueryFilter_Excludes_IsDeleted_Content()
    {
        using var context = CreateContext();
        var active = new Content { Title = "Active" };
        var deleted = new Content { Title = "Deleted", IsDeleted = true };

        context.Contents.AddRange(active, deleted);
        await context.SaveChangesAsync();

        var results = await context.Contents.ToListAsync();
        Assert.Single(results);
        Assert.Equal("Active", results[0].Title);
    }

    [Fact]
    public async Task SoftDelete_Filter_Can_Be_Overridden_With_IgnoreQueryFilters()
    {
        using var context = CreateContext();
        var active = new Content { Title = "Active" };
        var deleted = new Content { Title = "Deleted", IsDeleted = true };

        context.Contents.AddRange(active, deleted);
        await context.SaveChangesAsync();

        var results = await context.Contents.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void ContentPlatformPublish_Has_Composite_Index_On_Platform_Status()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(ContentPlatformPublish))!;
        var indexes = entity.GetIndexes().ToList();

        var compositeIndex = indexes.FirstOrDefault(i =>
        {
            var props = i.Properties.Select(p => p.Name).ToList();
            return props.Contains(nameof(ContentPlatformPublish.Platform))
                && props.Contains(nameof(ContentPlatformPublish.Status));
        });

        Assert.NotNull(compositeIndex);
    }

    [Fact]
    public void BrandProfile_Has_Seeded_Default_Row()
    {
        using var context = CreateContext();
        var modelSource = context.GetService<IModelSource>();
        var dependencies = context.GetService<ModelCreationDependencies>();
        var designTimeModel = modelSource.GetModel(context, dependencies, designTime: true);
        var entity = designTimeModel.FindEntityType(typeof(BrandProfile))!;
        var seedData = entity.GetSeedData().ToList();

        Assert.Single(seedData);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), seedData[0][nameof(BrandProfile.Id)]);
    }
}
