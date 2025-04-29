using System.Security.Cryptography;
using System.Text;

namespace Storage.Utils;

internal sealed class Signature(string region, string service, string secretKey)
{
	private static IArrayPool ArrayPool => DefaultArrayPool.Instance;

	public const string Iso8601DateTime = "yyyyMMddTHHmmssZ";
	public const string Iso8601Date = "yyyyMMdd";

	private static SortedDictionary<string, string>? _headerSort = [];




	private readonly byte[] _secretKey = Encoding.UTF8.GetBytes($"AWS4{secretKey}");
	private readonly string _scope = $"/{region}/{service}/aws4_request\n";

	public string Calculate(HttpRequestMessage request, string payloadHash, DateTimeOffset requestDate)
		=> Calculate(request, payloadHash, HeadBuilder.S3Headers, requestDate);


	[SkipLocalsInit]
	public string Calculate(
		HttpRequestMessage request,
		string payloadHash,
		string[] signedHeaders,
		DateTimeOffset requestDate)
	{
		var builder = new ValueStringBuilder(stackalloc char[512]);

		AppendStringToSign(ref builder, requestDate);
		AppendCanonicalRequestHash(ref builder, request, signedHeaders, payloadHash);

		Span<byte> signature = stackalloc byte[32];
		CreateSigningKey(ref signature, requestDate);

		signature = signature[..Sign(ref signature, signature, builder.AsReadonlySpan())];
		builder.Dispose();

		return HashHelper.ToHex(signature);
	}


	[SkipLocalsInit]
	public string Calculate(string url, DateTimeOffset requestDate)
	{
		var builder = new ValueStringBuilder(stackalloc char[512]);

		AppendStringToSign(ref builder, requestDate);
		AppendCanonicalRequestHash(ref builder, url);

		Span<byte> signature = stackalloc byte[32];
		CreateSigningKey(ref signature, requestDate);

		signature = signature[..Sign(ref signature, signature, builder.AsReadonlySpan())];
		builder.Dispose();

		return HashHelper.ToHex(signature);
	}

	private void AppendCanonicalHeaders(
		scoped ref ValueStringBuilder builder,
		HttpRequestMessage request,
		string[] signedHeaders)
	{
		var sortedHeaders = Interlocked.Exchange(ref _headerSort, null) ?? [];
		foreach (var requestHeader in request.Headers)
		{
			var header = NormalizeHeader(requestHeader.Key);
			if (signedHeaders.Contains(header))
			{
				sortedHeaders.Add(header, string.Join(' ', requestHeader.Value).Trim());
			}
		}

		var content = request.Content;
		if (content != null)
		{
			foreach (var contentHeader in content.Headers)
			{
				var header = NormalizeHeader(contentHeader.Key);
				if (signedHeaders.Contains(header))
				{
					sortedHeaders.Add(header, string.Join(' ', contentHeader.Value).Trim());
				}
			}
		}

		foreach (var (header, value) in sortedHeaders)
		{
			builder.Append(header);
			builder.Append(':');
			builder.Append(value);
			builder.Append('\n');
		}

		sortedHeaders.Clear();
		Interlocked.Exchange(ref _headerSort, sortedHeaders);
	}




	[SkipLocalsInit]
	private void AppendCanonicalRequestHash(
		scoped ref ValueStringBuilder builder,
		HttpRequestMessage request,
		string[] signedHeaders,
		string payload)
	{
		var canonical = new ValueStringBuilder(stackalloc char[512]);
		var uri = request.RequestUri!;

		const char newLine = '\n';

		canonical.Append(request.Method.Method);
		canonical.Append(newLine);
		canonical.Append(uri.AbsolutePath);
		canonical.Append(newLine);
		UrlUtils.AppendCanonicalQueryParameters(ref canonical, uri.Query);
		canonical.Append(newLine);

		AppendCanonicalHeaders(ref canonical, request, signedHeaders);
		canonical.Append(newLine);

		var first = true;
		var span = signedHeaders.AsSpan();
		for (var index = 0; index < span.Length; index++)
		{
			var header = span[index];
			if (first)
			{
				first = false;
			}
			else
			{
				canonical.Append(';');
			}

			canonical.Append(header);
		}

		canonical.Append(newLine);
		canonical.Append(payload);

		AppendSha256ToHex(ref builder, canonical.AsReadonlySpan());

		canonical.Dispose();
	}


	[SkipLocalsInit]
	private void AppendCanonicalRequestHash(scoped ref ValueStringBuilder builder, string url)
	{
		var uri = new Uri(url);

		var canonical = new ValueStringBuilder(stackalloc char[256]);
		canonical.Append("GET\n"); // canonical request
		canonical.Append(uri.AbsolutePath);
		canonical.Append('\n');
		canonical.Append(uri.Query.AsSpan(1));
		canonical.Append('\n');
		canonical.Append("host:");
		canonical.Append(uri.Host);

		if (!uri.IsDefaultPort)
		{
			canonical.Append(':');
			canonical.Append(uri.Port);
		}

		canonical.Append("\n\n");
		canonical.Append("host\n");
		canonical.Append("UNSIGNED-PAYLOAD");

		AppendSha256ToHex(ref builder, canonical.AsReadonlySpan());

		canonical.Dispose();
	}

	[SkipLocalsInit]
	private void AppendSha256ToHex(ref ValueStringBuilder builder, scoped ReadOnlySpan<char> value)
	{
		var count = Encoding.UTF8.GetByteCount(value);

		var byteBuffer = ArrayPool.Rent<byte>(count);

		var encoded = Encoding.UTF8.GetBytes(value, byteBuffer);

		Span<byte> hashBuffer = stackalloc byte[32];
		if (SHA256.TryHashData(byteBuffer.AsSpan(0, encoded), hashBuffer, out var written))
		{
			Span<char> buffer = stackalloc char[2];
			for (var index = 0; index < hashBuffer[..written].Length; index++)
			{
				var element = hashBuffer[..written][index];
				builder.Append(buffer[..StringUtils.FormatX2(ref buffer, element)]);
			}
		}

		ArrayPool.Return(byteBuffer);
	}

	[SkipLocalsInit]
	private string NormalizeHeader(string header)
	{
		using var builder = new ValueStringBuilder(stackalloc char[header.Length]);
		var culture = CultureInfo.InvariantCulture;

		// ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
		var span = header.AsSpan();
		foreach (var ch in span)
		{
			if (ch is ' ') continue;
			builder.Append(char.ToLower(ch, culture));
		}

		return string.Intern(builder.Flush());
	}

	private static int Sign(ref Span<byte> buffer, ReadOnlySpan<byte> key, scoped ReadOnlySpan<char> content)
	{
		var count = Encoding.UTF8.GetByteCount(content);

		var byteBuffer = ArrayPool.Rent<byte>(count);

		var encoded = Encoding.UTF8.GetBytes(content, byteBuffer);
		var result = HMACSHA256.TryHashData(key, byteBuffer.AsSpan(0, encoded), buffer, out var written)
			? written
			: -1;

		ArrayPool.Return(byteBuffer);

		return result;
	}



	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void AppendStringToSign(ref ValueStringBuilder builder, DateTimeOffset requestDate)
	{
		builder.Append("AWS4-HMAC-SHA256\n");
		builder.Append(requestDate, Iso8601DateTime);
		builder.Append("\n");
		builder.Append(requestDate, Iso8601Date);
		builder.Append(_scope);
	}

	[SkipLocalsInit]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void CreateSigningKey(ref Span<byte> buffer, DateTimeOffset requestDate)
	{
		Span<char> dateBuffer = stackalloc char[16];

		Sign(ref buffer, _secretKey, dateBuffer[..StringUtils.Format(ref dateBuffer, requestDate, Iso8601Date)]);
		Sign(ref buffer, buffer, region);
		Sign(ref buffer, buffer, service);
		Sign(ref buffer, buffer, "aws4_request");
	}
}
