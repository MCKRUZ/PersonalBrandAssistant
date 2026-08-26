using System.Buffers.Binary;

namespace PBA.Infrastructure.Media;

/// <summary>
/// Minimal MP4 container probe: reads the movie duration from the <c>moov/mvhd</c> box without
/// decoding any video or shelling out to ffmpeg. Used to choose a cover-frame offset that isn't the
/// (often black title-card) first frame.
/// </summary>
internal static class Mp4Probe
{
    /// <summary>
    /// Reads the total duration of an MP4 in milliseconds. Returns false if the bytes are not a
    /// parseable MP4 (e.g. truncated, or a different container) — callers fall back to a default.
    /// </summary>
    public static bool TryGetDurationMs(ReadOnlySpan<byte> data, out int durationMs)
    {
        durationMs = 0;

        if (!TryFindBox(data, "moov", out var moov) ||
            !TryFindBox(moov, "mvhd", out var mvhd) ||
            mvhd.Length < 20)
            return false;

        var version = mvhd[0];
        uint timescale;
        ulong duration;

        if (version == 1)
        {
            // version(1) flags(3) creation(8) modification(8) timescale(4) duration(8)
            if (mvhd.Length < 32) return false;
            timescale = BinaryPrimitives.ReadUInt32BigEndian(mvhd.Slice(20, 4));
            duration = BinaryPrimitives.ReadUInt64BigEndian(mvhd.Slice(24, 8));
        }
        else
        {
            // version(1) flags(3) creation(4) modification(4) timescale(4) duration(4)
            timescale = BinaryPrimitives.ReadUInt32BigEndian(mvhd.Slice(12, 4));
            duration = BinaryPrimitives.ReadUInt32BigEndian(mvhd.Slice(16, 4));
        }

        if (timescale == 0) return false;
        var seconds = (double)duration / timescale;
        if (seconds is <= 0 or double.NaN) return false;

        durationMs = (int)(seconds * 1000);
        return durationMs > 0;
    }

    /// <summary>
    /// Scans the sibling boxes inside <paramref name="container"/> for the first box of
    /// <paramref name="type"/> and returns its payload (the bytes after the box header). Handles
    /// 64-bit (largesize) and to-end (size 0) boxes, and tolerates a truncated final box.
    /// </summary>
    private static bool TryFindBox(ReadOnlySpan<byte> container, string type, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        var offset = 0;

        while (offset + 8 <= container.Length)
        {
            var size32 = BinaryPrimitives.ReadUInt32BigEndian(container.Slice(offset, 4));
            var boxType = container.Slice(offset + 4, 4);

            ulong boxSize;
            int payloadStart;
            if (size32 == 1)
            {
                if (offset + 16 > container.Length) return false;
                boxSize = BinaryPrimitives.ReadUInt64BigEndian(container.Slice(offset + 8, 8));
                payloadStart = offset + 16;
            }
            else if (size32 == 0)
            {
                boxSize = (ulong)(container.Length - offset); // extends to end
                payloadStart = offset + 8;
            }
            else
            {
                boxSize = size32;
                payloadStart = offset + 8;
            }

            if (boxSize < 8) return false;

            var boxEnd = offset + (long)boxSize;
            if (boxEnd > container.Length) boxEnd = container.Length; // tolerate truncation

            if (IsType(boxType, type))
            {
                if (payloadStart > boxEnd) return false;
                payload = container[payloadStart..(int)boxEnd];
                return true;
            }

            offset = (int)boxEnd;
        }

        return false;
    }

    private static bool IsType(ReadOnlySpan<byte> b, string type) =>
        b.Length == 4 && b[0] == type[0] && b[1] == type[1] && b[2] == type[2] && b[3] == type[3];
}
