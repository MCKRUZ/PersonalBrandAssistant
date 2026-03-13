using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Tests.Common;

public class EntityBaseTests
{
    private class TestEntity : EntityBase;

    [Fact]
    public void Id_IsValidUuidV7()
    {
        var entity = new TestEntity();
        var bytes = entity.Id.ToByteArray();
        // UUIDv7: version nibble at byte[7] high nibble should be 0x70
        var version = (bytes[7] >> 4) & 0x0F;
        Assert.Equal(7, version);
    }

    [Fact]
    public void SequentialEntities_HaveChronologicallyOrderedIds()
    {
        var first = new TestEntity();
        var second = new TestEntity();
        // UUIDv7 string representation sorts chronologically
        Assert.True(
            string.Compare(second.Id.ToString(), first.Id.ToString(), StringComparison.Ordinal) >= 0);
    }
}
