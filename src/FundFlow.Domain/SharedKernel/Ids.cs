using System.Security.Cryptography;

namespace FundFlow.Domain.SharedKernel;

/// <summary>
/// Generates identifiers that are unique across processes yet roughly time-ordered <em>in SQL Server's
/// uniqueidentifier sort order</em>. SQL Server compares the last six bytes of the storage layout first, so a
/// millisecond timestamp is written there. This keeps clustered-index inserts append-mostly (no page-split storms)
/// at the 10M+ row volumes FundFlow targets, while still letting the domain assign ids before persistence.
/// </summary>
public static class Ids
{
    public static Guid New() => New(DateTimeOffset.UtcNow);

    public static Guid New(DateTimeOffset timestamp)
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes[..10]);

        var milliseconds = (ulong)timestamp.ToUnixTimeMilliseconds();
        // Big-endian 48-bit timestamp in bytes 10..15 == the most significant bytes for SQL Server ordering.
        bytes[10] = (byte)(milliseconds >> 40);
        bytes[11] = (byte)(milliseconds >> 32);
        bytes[12] = (byte)(milliseconds >> 24);
        bytes[13] = (byte)(milliseconds >> 16);
        bytes[14] = (byte)(milliseconds >> 8);
        bytes[15] = (byte)milliseconds;

        return new Guid(bytes);
    }

    /// <summary>Extracts the embedded timestamp bytes (used by tests to verify ordering).</summary>
    public static long ExtractTimestampMilliseconds(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        long value = 0;
        for (var i = 10; i < 16; i++)
        {
            value = (value << 8) | bytes[i];
        }

        return value;
    }
}
