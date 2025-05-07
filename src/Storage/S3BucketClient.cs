using Storage.Utils;
using static Storage.Utils.HashHelper;

namespace Storage;

/// <summary>
/// Клиент для загрузки данных в S3 и их получения
/// </summary>
[DebuggerDisplay("Client for '{Bucket}'")]
[SuppressMessage("ReSharper", "SwitchStatementHandlesSomeKnownEnumValuesWithDefault", Justification = "Approved")]
[SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:Fields should be private", Justification = "Approved")]
public partial class S3BucketClient : IS3BucketClient, IDisposable
{
	internal const int DefaultPartSize = 5 * 1024 * 1024; // 5 Mb

	private readonly IArrayPool _arrayPool = DefaultArrayPool.Instance;

	private readonly string _bucket;
	private readonly HttpClient _client;
	private readonly string _host;
	private readonly Signature _signature;
	private readonly HeadBuilder _headBuilder;
	private readonly bool _useHttp2;

	private readonly string _urlMiddle;
	private readonly string _urlStart;

	private readonly TimeProvider _timeProvider;

	private bool _disposed;

	public string Bucket { get; }
	public Uri Endpoint { get; }

	public S3BucketClient(HttpClient client, S3BucketSettings settings, TimeProvider? timeProvider = null)
	{
		Bucket = settings.Bucket ?? throw new ArgumentException(nameof(settings.Bucket));
		Endpoint = new Uri(settings.Endpoint);

		_timeProvider = timeProvider ?? TimeProvider.System;

		_bucket = $"{Endpoint.AbsoluteUri}{Bucket.ToLowerInvariant()}";
		_client = client;
		_host = $"{Endpoint.Host}:{Endpoint.Port}";
		_useHttp2 = settings.UseHttp2;

		_urlMiddle = $"%2F{settings.Region}%2F{settings.Service}%2Faws4_request";
		_urlStart = $"?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential={settings.AccessKey}%2F";

		_signature = new Signature(settings.Region, settings.Service, settings.SecretKey);
		_headBuilder = new HeadBuilder(settings.AccessKey, settings.Region, settings.Service);
	}

	/// <summary>
	/// Создаёт подписанную ссылку на файл без проверки наличия файла на сервере
	/// </summary>
	/// <param name="fileName">Название файла</param>
	/// <param name="expiration">Время жизни ссылки</param>
	/// <returns>Возвращает подписанную ссылку на файл</returns>
	public string BuildFileUrl(string fileName, TimeSpan expiration)
	{
		var now = _timeProvider.GetUtcNow();
		var url = BuildFileUrl(_bucket, fileName, now, expiration);
		var signature = _signature.Calculate(url, now);

		return $"{url}&X-Amz-Signature={signature}";
	}

	public string BuildFileUrl(string bucket, string fileName, DateTimeOffset now, TimeSpan expires)
	{
		var builder = new ValueStringBuilder(stackalloc char[512]);

		builder.Append(bucket);
		builder.Append('/');
		UrlUtils.AppendEncodedName(ref builder, fileName);

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

	public async Task DeleteFile(string fileName, CancellationToken ct)
	{
		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Delete, fileName))
		{
			response = await Send(request, EmptyPayloadHash, ct).ConfigureAwait(false);
		}

		if (response.StatusCode is not HttpStatusCode.NoContent)
		{
			Errors.UnexpectedResult(response);
		}

		response.Dispose();
	}


	/// <summary>
	/// Получает объектное представление файла на сервере
	/// </summary>
	/// <param name="fileName">Название файла</param>
	/// <param name="ct">Токен отмены операции</param>
	/// <returns>Возвращает объектное представление файла на сервере</returns>
	public async Task<S3File> GetFile(string fileName, CancellationToken ct)
	{
		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Get, fileName))
		{
			response = await Send(request, EmptyPayloadHash, ct).ConfigureAwait(false);
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

	public async Task<Stream> GetFileStream(string fileName, CancellationToken ct)
	{
		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Get, fileName))
		{
			response = await Send(request, EmptyPayloadHash, ct).ConfigureAwait(false);
		}

		switch (response.StatusCode)
		{
			case HttpStatusCode.OK:
				return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
			case HttpStatusCode.NotFound:
				response.Dispose();
				return Stream.Null;
			default:
				Errors.UnexpectedResult(response);
				return Stream.Null;
		}
	}

	/// <summary>
	/// Создаёт подписанную ссылку на файл после проверки наличия файла на сервере
	/// </summary>
	/// <param name="fileName">Название файла</param>
	/// <param name="expiration">Время жизни ссылки</param>
	/// <param name="ct">Токен отмены операции</param>
	/// <returns>Возвращает подписанную ссылку на файл</returns>
	public async Task<string?> GetFileUrl(string fileName, TimeSpan expiration, CancellationToken ct)
	{
		return await IsFileExists(fileName, ct).ConfigureAwait(false)
			? BuildFileUrl(fileName, expiration)
			: null;
	}

	public async Task<bool> IsFileExists(string fileName, CancellationToken ct)
	{
		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Head, fileName))
		{
			response = await Send(request, EmptyPayloadHash, ct).ConfigureAwait(false);
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

	public async IAsyncEnumerable<string> List(string? prefix, [EnumeratorCancellation] CancellationToken ct)
	{
		var url = string.IsNullOrEmpty(prefix)
			? $"{_bucket}?list-type=2"
			: $"{_bucket}?list-type=2&prefix={UrlUtils.UrlEncodeName(prefix)}";

		HttpResponseMessage response;
		using (var request = new HttpRequestMessage(HttpMethod.Get, url))
		{
			response = await Send(request, EmptyPayloadHash, ct).ConfigureAwait(false);
		}

		if (response.StatusCode is HttpStatusCode.OK)
		{
			var responseStream = await response.Content
				.ReadAsStreamAsync(ct)
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

	/// <summary>
	/// Загружает файл с ручным управлением загрузкой блоков файла
	/// </summary>
	/// <param name="fileName">Название файла</param>
	/// <param name="contentType">Тип загружаемого файла</param>
	/// <param name="ct">Токен отмены операции</param>
	/// <returns>Возвращает объект управления загрузкой</returns>
	public async Task<S3Upload> UploadFile(string fileName, string contentType, CancellationToken ct)
	{
		return await MultipartStart(fileName, contentType, ct).ConfigureAwait(false);
	}

	/// <summary>
	/// Загружает файл на сервер
	/// </summary>
	/// <param name="fileName">Название файла</param>
	/// <param name="contentType">Тип загружаемого файла</param>
	/// <param name="data">Данные файл</param>
	/// <param name="ct">Токен отмены операции</param>
	/// <remarks>Если файл превышает 5 МБ, то будет применена Multipart-загрузка</remarks>
	/// <returns>Возвращает результат загрузки файла</returns>
	public Task<bool> UploadFile(string fileName, string contentType, Stream data, CancellationToken ct)
	{
		var length = data.TryGetLength();

		return length is null or 0 or > DefaultPartSize
			? ExecuteMultipartUpload(fileName, contentType, data, ct)
			: PutFile(fileName, contentType, data, ct);
	}

	/// <summary>
	/// Загружает файл на сервер
	/// </summary>
	/// <param name="fileName">Название файла</param>
	/// <param name="contentType">Тип загружаемого файла</param>
	/// <param name="data">Данные файл</param>
	/// <param name="ct">Токен отмены операции</param>
	/// <remarks>Если файл превышает 5 МБ, то будет применена Multipart-загрузка</remarks>
	/// <returns>Возвращает результат загрузки файла</returns>
	public Task<bool> UploadFile(string fileName, string contentType, byte[] data, CancellationToken ct)
	{
		var length = data.Length;

		return length > DefaultPartSize
			? ExecuteMultipartUpload(fileName, contentType, data, ct)
			: PutFile(fileName, contentType, data, data.Length, ct);
	}

	private async Task<bool> PutFile(
		string fileName,
		string contentType,
		Stream data,
		CancellationToken ct)
	{
		var buffer = _arrayPool.Rent<byte>((int)data.Length); // размер точно есть
		var dataSize = await data.ReadAsync(buffer, ct).ConfigureAwait(false);

		var payloadHash = GetPayloadHash(buffer.AsSpan(0, dataSize));

		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Put, fileName))
		{
			using var content = new ByteArrayContent(buffer, 0, dataSize);
			content.Headers.Add("content-type", contentType);
			request.Content = content;

			try
			{
				response = await Send(request, payloadHash, ct).ConfigureAwait(false);
			}
			finally
			{
				_arrayPool.Return(buffer);
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

	private async Task<bool> PutFile(
		string fileName,
		string contentType,
		byte[] data,
		int length,
		CancellationToken ct)
	{
		var payloadHash = GetPayloadHash(data);

		HttpResponseMessage response;
		using (var request = CreateRequest(HttpMethod.Put, fileName))
		{
			using var content = new ByteArrayContent(data, 0, length);
			content.Headers.Add("content-type", contentType);
			request.Content = content;

			response = await Send(request, payloadHash, ct).ConfigureAwait(false);
		}

		if (response.StatusCode is HttpStatusCode.OK)
		{
			response.Dispose();
			return true;
		}

		Errors.UnexpectedResult(response);
		return false;
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_disposed)
		{
			if (disposing)
			{
				_client.Dispose();
			}
			_disposed = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
