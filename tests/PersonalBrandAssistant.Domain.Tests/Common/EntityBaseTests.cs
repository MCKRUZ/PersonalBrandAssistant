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
        Thread.Sleep(2); // Ensure different millisecond timestamps
        var second = new TestEntity();

        // Compare UUIDv7 timestamps via big-endian byte layout (first 6 bytes = 48-bit Unix ms)
        var firstBytes = first.Id.ToByteArray();
        var secondBytes = second.Id.ToByteArray();

        // .NET Guid.ToByteArray() uses mixed-endian; use ToString("N") and parse hex for reliable comparison
        var firstHex = first.Id.ToString("N");
        var secondHex = second.Id.ToString("N");

        // First 12 hex chars = 48-bit timestamp; these should be non-decreasing
        var firstTimestamp = firstHex[..12];
        var secondTimestamp = secondHex[..12];
        Assert.True(
            string.Compare(secondTimestamp, firstTimestamp, StringComparison.Ordinal) >= 0,
            $"Expected second timestamp >= first: {secondTimestamp} vs {firstTimestamp}");
    }
}
