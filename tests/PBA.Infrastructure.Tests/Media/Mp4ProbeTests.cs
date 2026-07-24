using System.Buffers.Binary;
using PBA.Infrastructure.Media;
using Xunit;

namespace PBA.Infrastructure.Tests.Media;

public class Mp4ProbeTests
{
    [Fact]
    public void TryGetDurationMs_ValidMp4_ReadsDurationFromMvhd()
    {
        // ftyp (skipped) + moov>mvhd with timescale 1000, duration 60000 => 60 seconds.
        var mvhd = Mvhd(timescale: 1000, duration: 60000);
        var moov = Box("moov", mvhd);
        var mp4 = Concat(Box("ftyp", new byte[] { 0x69, 0x73, 0x6F, 0x6D, 0, 0, 0, 0 }), moov);

        var ok = Mp4Probe.TryGetDurationMs(mp4, out var ms);

        Assert.True(ok);
        Assert.Equal(60_000, ms);
    }

    [Fact]
    public void TryGetDurationMs_MoovAfterMdat_StillFound()
    {
        // Non-faststart layout: a large mdat precedes moov. The scanner must skip mdat by its size.
        var mdat = Box("mdat", new byte[256]);
        var moov = Box("moov", Mvhd(timescale: 600, duration: 18_000)); // 30 seconds
        var mp4 = Concat(mdat, moov);

        Assert.True(Mp4Probe.TryGetDurationMs(mp4, out var ms));
        Assert.Equal(30_000, ms);
    }

    [Fact]
    public void TryGetDurationMs_NotAnMp4_ReturnsFalse()
    {
        Assert.False(Mp4Probe.TryGetDurationMs(new byte[16], out var ms));
        Assert.Equal(0, ms);
    }

    private static byte[] Mvhd(uint timescale, uint duration)
    {
        var payload = new byte[20];
        // version(1)+flags(3) = 0; creation(4)=0; modification(4)=0
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(12, 4), timescale);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(16, 4), duration);
        return Box("mvhd", payload);
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
        for (var i = 0; i < 4; i++) box[4 + i] = (byte)type[i];
        payload.CopyTo(box, 8);
        return box;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var p in parts) { p.CopyTo(result, offset); offset += p.Length; }
        return result;
    }
}
