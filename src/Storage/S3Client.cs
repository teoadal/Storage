using System.Buffers;
using System.Globalization;
using System.Text;
using Storage.Utils;
using static Storage.Utils.HashHelper;

namespace Storage;

[DebuggerDisplay("Client for '{Bucket}'")]
[SuppressMessage("ReSharper", "SwitchStatementHandlesSomeKnownEnumValuesWithDefault", Justification = "Approved")]
[SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:Fields should be private", Justification = "Approved")]
public sealed class S3Client
{
	internal const int DefaultPartSize = 5 * 1024 * 1024; // 5 Mb

	internal readonly string Bucket;

	private static readonly string[] _s3Headers = // trimmed, lower invariant, ordered
	[
		"host",
		"x-amz-content-sha256",
		"x-amz-date",
	];

	private readonly string _bucket;
	private readonly string _endpoint;
	private readonly HttpHelper _http;
	private readonly HttpClient _client;
	private readonly Signature _signature;
	private readonly bool _useHttp2;

	private bool _disposed;

	public S3Client(S3Settings settings, HttpClient? client = null)
	{
		Bucket = settings.Bucket;

		var bucket = Bucket.ToLowerInvariant();
		var scheme = settings.UseHttps ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
		var port = settings.Port.HasValue ? $":{settings.Port}" : string.Empty;

		_bucket = $"{scheme}://{settings.EndPoint}{port}/{bucket}";
		_client = client ?? new HttpClient();
		_endpoint = $"{settings.EndPoint}{port}";
		_http = new HttpHelper(settings.AccessKey, settings.Region, settings.Service, _s3Headers);
		_signature = new Signature(settings.SecretKey, settings.Region, settings.Service);
		_useHttp2 = settings.UseHttp2;
	}

	public string BuildFileUrl(string fileName, TimeSpan expiration)
	{
		var now = DateTime.UtcNow;
		var url = _http.BuildUrl(_bucket, fileName, now, expiration);
		var signature = _signature.Calculate(url, now);

		return $"{url}&X-Amz-Signature={signature}";
	}

	public async Task<bool> CreateBucket(CancellationToken cancellation)
	{
		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Put))
		{
			response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
		}

		switch (response.StatusCode)
		{
			case HttpStatusCode.OK:
				response.Dispose();
				return true;
			case HttpStatusCode.Conflict: // already exists
				response.Dispose();
				return false;
			default:
				Errors.UnexpectedResult(response);
				return false;
		}
	}

	public async Task<bool> DeleteBucket(CancellationToken cancellation)
	{
		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Delete))
		{
			response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
		}

		switch (response.StatusCode)
		{
			case HttpStatusCode.NoContent:
				response.Dispose();
				return true;
			case HttpStatusCode.NotFound:
				response.Dispose();
				return false;
			default:
				Errors.UnexpectedResult(response);
				return false;
		}
	}

	public async Task DeleteFile(string fileName, CancellationToken cancellation)
	{
		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Delete, fileName))
		{
			response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
		}

		if (response.StatusCode is not HttpStatusCode.NoContent)
		{
			Errors.UnexpectedResult(response);
		}

		response.Dispose();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_client.Dispose();

		_disposed = true;
	}

	public async Task<S3File> GetFile(string fileName, CancellationToken cancellation)
	{
		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Get, fileName))
		{
			response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
		}

		switch (response.StatusCode)
		{
			case HttpStatusCode.OK:
				return new S3File(response);
			case HttpStatusCode.NotFound:
				response.Dispose();
				return new S3File(response);
			default:
				Errors.UnexpectedResult(response);
				return new S3File(null!); // никогда не будет вызвано
		}
	}

	public async Task<Stream> GetFileStream(string fileName, CancellationToken cancellation)
	{
		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Get, fileName))
		{
			response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
		}

		switch (response.StatusCode)
		{
			case HttpStatusCode.OK:
				return await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
			case HttpStatusCode.NotFound:
				response.Dispose();
				return Stream.Null;
			default:
				Errors.UnexpectedResult(response);
				return Stream.Null;
		}
	}

	public async Task<string?> GetFileUrl(string fileName, TimeSpan expiration, CancellationToken cancellation)
	{
		return await IsFileExists(fileName, cancellation).ConfigureAwait(false)
			? BuildFileUrl(fileName, expiration)
			: null;
	}

	public async Task<bool> IsBucketExists(CancellationToken cancellation)
	{
		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Head))
		{
			response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
		}

		switch (response.StatusCode)
		{
			case HttpStatusCode.OK:
				response.Dispose();
				return true;
			case HttpStatusCode.NotFound:
				response.Dispose();
				return false;
			default:
				Errors.UnexpectedResult(response);
				return false;
		}
	}

	public async Task<bool> IsFileExists(string fileName, CancellationToken cancellation)
	{
		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Head, fileName))
		{
			response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
		}

		switch (response.StatusCode)
		{
			case HttpStatusCode.OK:
				response.Dispose();
				return true;
			case HttpStatusCode.NotFound:
				response.Dispose();
				return false;
			default:
				Errors.UnexpectedResult(response);
				return false;
		}
	}

	public async IAsyncEnumerable<string> List(string? prefix, [EnumeratorCancellation] CancellationToken cancellation)
	{
		var url = string.IsNullOrEmpty(prefix)
			? $"{_bucket}?list-type=2"
			: $"{_bucket}?list-type=2&prefix={HttpHelper.EncodeName(prefix)}";

		HttpResponseMessage response;
		using (var request = new HttpRequestMessage(HttpMethod.Get, url))
		{
			response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
		}

		if (response.StatusCode is HttpStatusCode.OK)
		{
			var responseStream = await response.Content
				.ReadAsStreamAsync(cancellation)
				.ConfigureAwait(false);

			while (responseStream.CanRead)
			{
				var readString = XmlStreamReader.ReadString(responseStream, "Key");
				if (string.IsNullOrEmpty(readString))
				{
					break;
				}

				yield return readString;
			}

			await responseStream.DisposeAsync().ConfigureAwait(false);
			response.Dispose();

			yield break;
		}

		Errors.UnexpectedResult(response);
	}

	public Task<bool> UploadFile(string fileName, string contentType, Stream data, CancellationToken cancellation)
	{
		var length = data.TryGetLength();

		return length is null or 0 or > DefaultPartSize
			? PutFileMultipart(fileName, contentType, data, cancellation)
			: PutFile(fileName, contentType, data, cancellation);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Task<bool> UploadFile(string fileName, string contentType, byte[] data, CancellationToken cancellation)
	{
		return UploadFile(fileName, contentType, data, 0, data.Length, cancellation);
	}

	internal async Task<bool> MultipartAbort(string encodedFileName, string uploadId, CancellationToken ct)
	{
		var url = $"{_bucket}/{encodedFileName}?uploadId={uploadId}";

		HttpResponseMessage? response = null;
		using (var request = new HttpRequestMessage(HttpMethod.Delete, url))
		{
			try
			{
				response = await Send(request, EmptyPayloadHash, ct).ConfigureAwait(false);
			}
			catch
			{
				// ignored
			}
		}

		if (response is null)
		{
			return false;
		}

#pragma warning disable CA1508
		var result = response is
		{
			IsSuccessStatusCode: true,
			StatusCode: HttpStatusCode.NoContent,
		};
#pragma warning restore CA1508

		response.Dispose();

		return result;
	}

	internal async Task<bool> MultipartComplete(
		string encodedFileName,
		string uploadId,
		string[] partTags,
		int tagsCount,
		CancellationToken ct)
	{
		var builder = StringUtils.GetBuilder();

		builder.Append("<CompleteMultipartUpload>");
		for (var i = 0; i < partTags.Length; i++)
		{
			if (i == tagsCount)
			{
				break;
			}

			builder.Append("<Part>");
			builder.Append("<PartNumber>", i + 1, "</PartNumber>");
			builder.Append("<ETag>", partTags[i], "</ETag>");
			builder.Append("</Part>");
		}

		var data = builder
			.Append("</CompleteMultipartUpload>")
			.Flush();

		var payloadHash = GetPayloadHash(data);

		HttpResponseMessage response;
		using (var request = new HttpRequestMessage(
			       HttpMethod.Post,
			       $"{_bucket}/{encodedFileName}?uploadId={uploadId}"))
		{
			using (var content = new StringContent(data, Encoding.UTF8))
			{
				request.Content = content;
				response = await Send(request, payloadHash, ct).ConfigureAwait(false);
			}
		}

		var result = response is { IsSuccessStatusCode: true, StatusCode: HttpStatusCode.OK };

		response.Dispose();
		return result;
	}

	internal async Task<string?> MultipartUpload(
		string encodedFileName,
		string uploadId,
		int partNumber,
		byte[] partData,
		int partSize,
		CancellationToken ct)
	{
		var payloadHash = GetPayloadHash(partData.AsSpan(0, partSize));
		var url = $"{_bucket}/{encodedFileName}?partNumber={partNumber}&uploadId={uploadId}";

		HttpResponseMessage response;
		using (var request = new HttpRequestMessage(HttpMethod.Put, url))
		{
			using (var content = new ByteArrayContent(partData, 0, partSize))
			{
				content.Headers.Add("content-length", partSize.ToString());
				request.Content = content;

				response = await Send(request, payloadHash, ct).ConfigureAwait(false);
			}
		}

		var result = response is { IsSuccessStatusCode: true, StatusCode: HttpStatusCode.OK }
			? response.Headers.ETag?.Tag
			: null;

		response.Dispose();
		return result;
	}

	private async Task<bool> UploadFile(
		string fileName,
		string contentType,
		byte[] data,
		int offset,
		int count,
		CancellationToken cancellation)
	{
		var payloadHash = GetPayloadHash(data);

		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Put, fileName))
		{
			using (var content = new ByteArrayContent(data, offset, count))
			{
				content.Headers.Add("content-type", contentType);
				request.Content = content;

				response = await Send(request, payloadHash, cancellation).ConfigureAwait(false);
			}
		}

		if (response.StatusCode is HttpStatusCode.OK)
		{
			response.Dispose();
			return true;
		}

		Errors.UnexpectedResult(response);
		return false;
	}

	private async Task<S3Upload> UploadFile(string fileName, string contentType, CancellationToken cancellation)
	{
		var encodedFileName = HttpHelper.EncodeName(fileName);
		var uploadId = await MultipartStart(encodedFileName, contentType, cancellation);

		return new S3Upload(this, fileName, encodedFileName, uploadId);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private HttpRequestMessage CreateRequest(HttpMethod method, string? fileName = null)
	{
		var url = new ValueStringBuilder(stackalloc char[512]);
		url.Append(_bucket);

		// ReSharper disable once InvertIf
		if (!string.IsNullOrEmpty(fileName))
		{
			url.Append('/');
			HttpHelper.AppendEncodedName(ref url, fileName);
		}

		return new HttpRequestMessage(method, new Uri(url.Flush(), UriKind.Absolute));
	}

	private async Task<string> MultipartStart(string encodedFileName, string contentType, CancellationToken ct)
	{
		HttpResponseMessage response;
		using (var request = new HttpRequestMessage(HttpMethod.Post, $"{_bucket}/{encodedFileName}?uploads"))
		{
			using (var content = new ByteArrayContent(Array.Empty<byte>()))
			{
				content.Headers.Add("content-type", contentType);
				request.Content = content;

				response = await Send(request, EmptyPayloadHash, ct).ConfigureAwait(false);
			}
		}

		if (response.StatusCode is HttpStatusCode.OK)
		{
			var responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
			var result = XmlStreamReader.ReadString(responseStream, "UploadId");

			await responseStream.DisposeAsync().ConfigureAwait(false);
			response.Dispose();

			return result;
		}

		Errors.UnexpectedResult(response);
		return string.Empty;
	}

	private async Task<bool> PutFile(string fileName, string contentType, Stream data, CancellationToken ct)
	{
		var bufferPool = ArrayPool<byte>.Shared;

		var buffer = bufferPool.Rent((int)data.Length); // размер точно есть
		var dataSize = await data.ReadAsync(buffer, ct).ConfigureAwait(false);

		var payloadHash = GetPayloadHash(buffer.AsSpan(0, dataSize));

		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Put, fileName))
		{
			using (var content = new ByteArrayContent(buffer, 0, dataSize))
			{
				content.Headers.Add("content-type", contentType);
				request.Content = content;

				try
				{
					response = await Send(request, payloadHash, ct).ConfigureAwait(false);
				}
				finally
				{
					bufferPool.Return(buffer);
				}
			}
		}

		if (response.StatusCode == HttpStatusCode.OK)
		{
			response.Dispose();
			return true;
		}

		Errors.UnexpectedResult(response);
		return false;
	}

	private async Task<bool> PutFileMultipart(string fileName, string contentType, Stream data, CancellationToken ct)
	{
		using var upload = await UploadFile(fileName, contentType, ct);

		if (await upload.Upload(data, ct) && await upload.Complete(ct))
		{
			return true;
		}

		await upload.Abort(ct);
		return false;
	}

	private Task<HttpResponseMessage> Send(HttpRequestMessage request, string payloadHash, CancellationToken ct)
	{
		if (_disposed)
		{
			Errors.Disposed();
		}

		var now = DateTime.UtcNow;

		var headers = request.Headers;
		headers.Add("host", _endpoint);
		headers.Add("x-amz-content-sha256", payloadHash);
		headers.Add("x-amz-date", now.ToString(Signature.Iso8601DateTime, CultureInfo.InvariantCulture));

		if (_useHttp2)
		{
			request.Version = HttpVersion.Version20;
		}

		var signature = _signature.Calculate(request, payloadHash, _s3Headers, now);
		headers.TryAddWithoutValidation("Authorization", _http.BuildHeader(now, signature));

		return _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
	}
}
