using System.Security.Cryptography;
using System.Text;

namespace Storage.Utils;

internal static class HashHelper
{
	public static readonly string EmptyPayloadHash = Sha256ToHex(string.Empty, DefaultArrayPool.Instance);

	[SkipLocalsInit]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string GetPayloadHash(ReadOnlySpan<byte> data, IArrayPool arrayPool)
	{
		Span<byte> hashBuffer = stackalloc byte[32];
		return SHA256.TryHashData(data, hashBuffer, out _)
			? ToHex(hashBuffer, arrayPool)
			: string.Empty;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string GetPayloadHash(string data, IArrayPool arrayPool)
	{
		return Sha256ToHex(data, arrayPool);
	}

	[SkipLocalsInit]
	public static string ToHex(ReadOnlySpan<byte> data, IArrayPool arrayPool)
	{
		Span<char> buffer = stackalloc char[2];
		using var builder = new ValueStringBuilder(stackalloc char[64], arrayPool);

		foreach (ref readonly var element in data)
		{
			builder.Append(buffer[..StringUtils.FormatX2(ref buffer, element)]);
		}

		return builder.Flush();
	}

	[SkipLocalsInit]
	private static string Sha256ToHex(ReadOnlySpan<char> value, IArrayPool arrayPool)
	{
		var count = Encoding.UTF8.GetByteCount(value);

		var byteBuffer = arrayPool.Rent<byte>(count);

		var encoded = Encoding.UTF8.GetBytes(value, byteBuffer);
		Span<byte> hashBuffer = stackalloc byte[64];
		var result = SHA256.TryHashData(byteBuffer.AsSpan(0, encoded), hashBuffer, out var written)
			? ToHex(hashBuffer[..written], arrayPool)
			: string.Empty;

		arrayPool.Return(byteBuffer);

		return result;
	}
}
