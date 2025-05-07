using Storage.Utils;

namespace Storage;

/// <summary>
/// Структура управления Multipart-загрузкой
/// </summary>
public sealed class S3Upload : IDisposable
{

	private readonly S3BucketClient _client;
	private readonly string _encodedFileName;

	private byte[]? _byteBuffer;
	private bool _disposed;
	private int _partCount;
	private readonly IArrayPool _arrayPool;
	private string[] _parts;


	internal S3Upload(S3BucketClient client, string fileName, string encodedFileName, string uploadId)
	{
		FileName = fileName;
		UploadId = uploadId;

		_client = client;
		_encodedFileName = encodedFileName;

		_arrayPool = DefaultArrayPool.Instance;
		_parts = _arrayPool.Rent<string>(16);
	}

	/// <summary>
	/// Название загружаемого файла
	/// </summary>
	public string FileName
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get;
	}

	/// <summary>
	/// Идентификатор загрузки
	/// </summary>
	public string UploadId
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get;
	}

	/// <summary>
	/// Сколько байт загружено
	/// </summary>
	public long Written
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get;
		private set;
	}

	/// <summary>
	/// Прерывает загрузку и удалить временные данные с сервера
	/// </summary>
	/// <param name="ct">Токен отмены операции</param>
	/// <returns>Возвращает результат отмены загрузки</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Task<bool> Abort(CancellationToken ct)
	{
		if (_disposed)
		{
			Errors.Disposed();
		}

		return _client.MultipartAbort(_encodedFileName, UploadId, ct);
	}

	/// <summary>
	/// Загружает блок данных на сервер
	/// </summary>
	/// <param name="data">Блок данных</param>
	/// <param name="ct">Токен отмены операции</param>
	/// <returns>Возвращает результат загрузки</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Task<bool> AddPart(byte[] data, CancellationToken ct)
	{
		return AddPart(data, data.Length, ct);
	}

	/// <summary>
	/// Загружает блок данных указанного размера на сервер
	/// </summary>
	/// <param name="data">Блок данных</param>
	/// <param name="length">Количество данных, которые нужно взять из блока</param>
	/// <param name="ct">Токен отмены</param>
	/// <returns>Возвращает результат загрузки</returns>
	public async Task<bool> AddPart(byte[] data, int length, CancellationToken ct)
	{
		if (_disposed)
		{
			Errors.Disposed();
		}

		var partId = await _client.MultipartPutPart(
			_encodedFileName, UploadId, _partCount + 1, data, length, ct);

		if (string.IsNullOrEmpty(partId))
		{
			return false;
		}

		if (_parts.Length == _partCount)
		{
			CollectionUtils.Resize(ref _parts, _arrayPool, _partCount * 2);
		}

		_parts[_partCount++] = partId;

		Written += length;

		return true;
	}

	/// <summary>
	/// Разделяет данные на блоки и загружает их на сервер
	/// </summary>
	/// <param name="data">Блок данных</param>
	/// <param name="ct">Токен отмены операции</param>
	/// <returns>Возвращает результат загрузки</returns>
	public async Task<bool> AddParts(Stream data, CancellationToken ct)
	{
		_byteBuffer ??= _arrayPool.Rent<byte>(S3BucketClient.DefaultPartSize);

		while (true)
		{
			var written = await data.ReadTo(_byteBuffer, ct).ConfigureAwait(false);
			if (written is 0)
			{
				break;
			}

			if (!await AddPart(_byteBuffer, written, ct).ConfigureAwait(false))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Разделяет данные на блоки и загружает их на сервер
	/// </summary>
	/// <param name="data">Блок данных</param>
	/// <param name="ct">Токен отмены операции</param>
	/// <returns>Возвращает результат загрузки</returns>
	public async Task<bool> AddParts(byte[] data, CancellationToken ct)
	{
		_byteBuffer ??= ArrayPool<byte>.Shared.Rent(S3BucketClient.DefaultPartSize);

		var bufferLength = _byteBuffer.Length;
		var offset = 0;
		while (offset < data.Length)
		{
			var partSize = Math.Min(bufferLength, data.Length - offset);
			Array.Copy(data, offset, _byteBuffer, 0, partSize);

			if (!await AddPart(_byteBuffer, partSize, ct).ConfigureAwait(false))
			{
				return false;
			}

			offset += partSize;
		}

		return true;
	}

	/// <summary>
	/// Указывает серверу, что загрузка завершена и блоки данных можно объединить в файл
	/// </summary>
	/// <param name="ct">Токен отмены операции</param>
	/// <returns>Возвращает результат завершения загрузки</returns>
	public Task<bool> Complete(CancellationToken ct)
	{
		if (_disposed)
		{
			Errors.Disposed();
		}

		return _partCount == 0
			? Task.FromResult(false)
			: _client.MultipartComplete(_encodedFileName, UploadId, _parts, _partCount, ct);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		Array.Clear(_parts, 0, _partCount);
		_arrayPool.Return(_parts);
		_parts = null!;

		if (_byteBuffer is not null)
		{
			_arrayPool.Return(_byteBuffer);
			_byteBuffer = null;
		}

		_disposed = true;
	}
}
