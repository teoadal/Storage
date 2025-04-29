using System.Security.Cryptography;
using System.Text;

namespace Storage.Utils;

internal static class HashHelper
{
	public static IArrayPool ArrayPool => DefaultArrayPool.Instance;
	public static readonly string EmptyPayloadHash = Sha256ToHex(string.Empty);

	[SkipLocalsInit]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string GetPayloadHash(ReadOnlySpan<byte> data)
	{
		Span<byte> hashBuffer = stackalloc byte[32];
		return SHA256.TryHashData(data, hashBuffer, out _)
			? ToHex(hashBuffer)
			: string.Empty;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string GetPayloadHash(string data)
	{
		return Sha256ToHex(data);
	}

	[SkipLocalsInit]
	public static string ToHex(ReadOnlySpan<byte> data)
	{
		Span<char> buffer = stackalloc char[2];
		using var builder = new ValueStringBuilder(stackalloc char[64]);

		foreach (ref readonly var element in data)
		{
			builder.Append(buffer[..StringUtils.FormatX2(ref buffer, element)]);
		}

		return builder.Flush();
	}

	[SkipLocalsInit]
	private static string Sha256ToHex(ReadOnlySpan<char> value)
	{
		var count = Encoding.UTF8.GetByteCount(value);

		var byteBuffer = ArrayPool.Rent<byte>(count);

		var encoded = Encoding.UTF8.GetBytes(value, byteBuffer);
		Span<byte> hashBuffer = stackalloc byte[64];
		var result = SHA256.TryHashData(byteBuffer.AsSpan(0, encoded), hashBuffer, out var written)
			? ToHex(hashBuffer[..written])
			: string.Empty;

		ArrayPool.Return(byteBuffer);

		return result;
	}
}
