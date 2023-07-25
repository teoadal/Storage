﻿using System.Buffers;
using System.Text;

namespace Storage.Utils;

internal readonly struct HttpHelper
{
	private static readonly HashSet<char> _validUrlCharacters =
		"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~".ToHashSet();

	private readonly string _headerEnd;
	private readonly string _headerStart;

	private readonly string _urlMiddle;
	private readonly string _urlStart;

	public HttpHelper(string accessKey, string region, string service, string[] signedHeaders)
	{
		_headerStart = $"AWS4-HMAC-SHA256 Credential={accessKey}/";
		_headerEnd = $"/{region}/{service}/aws4_request, SignedHeaders={string.Join(';', signedHeaders)}, Signature=";

		_urlStart = $"?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential={accessKey}%2F";
		_urlMiddle = $"%2F{region}%2F{service}%2Faws4_request";
	}

	public static bool AppendEncodedName(scoped ref ValueStringBuilder builder, ReadOnlySpan<char> name)
	{
		var count = Encoding.UTF8.GetByteCount(name);
		var hasEncoded = false;

		var pool = ArrayPool<byte>.Shared;
		var byteBuffer = pool.Rent(count);

		Span<char> charBuffer = stackalloc char[2];
		Span<char> upperBuffer = stackalloc char[2];

		var validCharacters = _validUrlCharacters;
		var encoded = Encoding.UTF8.GetBytes(name, byteBuffer);

		try
		{
			var span = byteBuffer.AsSpan(0, encoded);
			for (var index = 0; index < span.Length; index++)
			{
				var symbol = (char)span[index];
				if (validCharacters.Contains(symbol))
				{
					builder.Append(symbol);
				}
				else
				{
					builder.Append('%');

					StringUtils.FormatX2(ref charBuffer, symbol);
					MemoryExtensions.ToUpperInvariant(charBuffer, upperBuffer);
					builder.Append(upperBuffer);

					hasEncoded = true;
				}
			}

			return hasEncoded;
		}
		finally
		{
			pool.Return(byteBuffer);
		}
	}

	public static string EncodeName(string fileName)
	{
		var builder = new ValueStringBuilder(stackalloc char[fileName.Length]);
		var encoded = AppendEncodedName(ref builder, fileName);

		return encoded
			? builder.Flush()
			: fileName;
	}

	public string BuildHeader(DateTime now, string signature)
	{
		using var builder = new ValueStringBuilder(stackalloc char[512]);

		builder.Append(_headerStart);
		builder.Append(now, Signature.Iso8601Date);
		builder.Append(_headerEnd);
		builder.Append(signature);

		return builder.Flush();
	}

	public string BuildUrl(string bucket, string fileName, DateTime now, TimeSpan expires)
	{
		var builder = new ValueStringBuilder(stackalloc char[512]);

		builder.Append(bucket);
		builder.Append('/');

		AppendEncodedName(ref builder, fileName);

		builder.Append(_urlStart);
		builder.Append(now, Signature.Iso8601Date);
		builder.Append(_urlMiddle);

		builder.Append("&X-Amz-Date=");
		builder.Append(now, Signature.Iso8601DateTime);
		builder.Append("&X-Amz-Expires=");
		builder.Append(expires.TotalSeconds);

		builder.Append("&X-Amz-SignedHeaders=host");

		return builder.Flush();
	}
}
