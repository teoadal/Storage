using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Storage.Utils;

internal static class HashHelper
{
    public static readonly string EmptyPayloadHash = Sha256ToHex(string.Empty);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetPayloadHash(ReadOnlySpan<byte> data)
    {
        Span<byte> hashBuffer = stackalloc byte[32];
        return SHA256.TryHashData(data, hashBuffer, out _)
            ? ToHex(hashBuffer)
            : string.Empty;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetPayloadHash(string data) => Sha256ToHex(data);

    [SuppressMessage("ReSharper", "InvertIf")]
    private static string Sha256ToHex(ReadOnlySpan<char> value)
    {
        var count = Encoding.UTF8.GetByteCount(value);
        using var memory = MemoryPool<byte>.Shared.Rent(count);
        if (MemoryMarshal.TryGetArray(memory.Memory[..count], out ArraySegment<byte> segment))
        {
            Encoding.UTF8.GetBytes(value, segment);
            Span<byte> hashBuffer = stackalloc byte[64];
            if (SHA256.TryHashData(segment, hashBuffer, out var written))
            {
                return ToHex(hashBuffer[..written]);
            }
        }

        return string.Empty;
    }

    public static string ToHex(ReadOnlySpan<byte> data)
    {
        Span<char> buffer = stackalloc char[2];
        var builder = new ValueStringBuilder(stackalloc char[64]);

        foreach (var element in data)
        {
            builder.Append(buffer[..StringUtils.FormatX2(ref buffer, element)]);
        }

        return builder.Flush();
    }
}