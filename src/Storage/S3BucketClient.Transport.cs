using Storage.Utils;

namespace Storage;

/// <summary>
/// Transport functions
/// </summary>
public partial class S3BucketClient
{
	[SkipLocalsInit]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private HttpRequestMessage CreateRequest(HttpMethod method, string? fileName = null)
	{
		var url = UrlUtils.BuildFileUrl(_bucket, fileName);
		return new HttpRequestMessage(method, new Uri(url, UriKind.Absolute));
	}

	private Task<HttpResponseMessage> Send(HttpRequestMessage request, string payloadHash, CancellationToken ct)
	{
		if (_disposed)
		{
			Errors.Disposed();
		}

		var now = _timeProvider.GetUtcNow();

		var headers = request.Headers;
		headers.Add("host", _host);
		headers.Add("x-amz-content-sha256", payloadHash);
		headers.Add("x-amz-date", now.ToString(Signature.Iso8601DateTime, CultureInfo.InvariantCulture));

		if (_useHttp2)
		{
			request.Version = HttpVersion.Version20;
		}

		var signature = _signature.Calculate(request, payloadHash, now);

		headers.TryAddWithoutValidation("Authorization", _headBuilder.BuildAuthorizationValue(now, signature));

		return _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
	}
}
